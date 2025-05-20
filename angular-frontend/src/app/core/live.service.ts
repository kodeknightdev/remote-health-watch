import { Injectable, NgZone, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { AuthService } from './auth.service';
import { AlertPush, LatestValue, RiskScore, VitalType, VitalsPush } from './models';

export const SPARKLINE_POINTS = 40;

type VitalMap = Partial<Record<VitalType, LatestValue>>;
type HistoryMap = Partial<Record<VitalType, number[]>>;

/** One SignalR connection for the whole app; components read signals rather than subscribing to the hub. */
@Injectable({ providedIn: 'root' })
export class LiveService {
  private readonly auth = inject(AuthService);
  private readonly zone = inject(NgZone);
  private connection: HubConnection | null = null;

  readonly state = signal<'disconnected' | 'connecting' | 'connected' | 'reconnecting'>('disconnected');
  readonly latest = signal<Record<string, VitalMap | undefined>>({});
  readonly history = signal<Record<string, HistoryMap | undefined>>({});
  readonly risk = signal<Record<string, RiskScore | undefined>>({});
  readonly alerts$ = new Subject<AlertPush>();
  /** push-to-browser latency of the last few alert events, for the status bar */
  readonly alertLatencyMs = signal<number | null>(null);

  async start(): Promise<void> {
    if (this.connection && this.connection.state !== HubConnectionState.Disconnected) return;
    this.connection = new HubConnectionBuilder()
      .withUrl('/hubs/live', { accessTokenFactory: async () => this.auth.token() ?? '' })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('vitals', (m: VitalsPush) => this.zone.run(() => this.onVitals(m)));
    this.connection.on('risk', (m: RiskScore) => this.zone.run(() => this.risk.update((r) => ({ ...r, [m.patientId]: m }))));
    this.connection.on('alert', (m: AlertPush) =>
      this.zone.run(() => {
        this.alertLatencyMs.set(Math.max(0, Date.now() - new Date(m.occurredAt).getTime()));
        this.alerts$.next(m);
      }),
    );
    this.connection.onreconnecting(() => this.zone.run(() => this.state.set('reconnecting')));
    this.connection.onreconnected(() => this.zone.run(() => this.state.set('connected')));
    this.connection.onclose(() => this.zone.run(() => this.state.set('disconnected')));

    this.state.set('connecting');
    try {
      await this.connection.start();
      this.state.set('connected');
    } catch {
      this.state.set('disconnected');
    }
  }

  async stop(): Promise<void> {
    await this.connection?.stop();
    this.connection = null;
  }

  watch(patientId: string): void {
    this.connection?.invoke('Watch', patientId).catch(() => undefined);
  }

  seedRisk(scores: RiskScore[]): void {
    this.risk.update((r) => {
      const next = { ...r };
      for (const s of scores) if (!next[s.patientId]) next[s.patientId] = s;
      return next;
    });
  }

  seedLatest(patientId: string, values: LatestValue[]): void {
    this.latest.update((all) => {
      const current: VitalMap = { ...(all[patientId] ?? {}) };
      for (const v of values) if (!current[v.vital] || current[v.vital]!.measuredAt < v.measuredAt) current[v.vital] = v;
      return { ...all, [patientId]: current };
    });
  }

  private onVitals(m: VitalsPush): void {
    this.seedLatest(m.patientId, m.readings);
    this.history.update((all) => {
      const current: HistoryMap = { ...(all[m.patientId] ?? {}) };
      const sorted = [...m.readings].sort((a, b) => a.measuredAt.localeCompare(b.measuredAt));
      for (const r of sorted) {
        const series = [...(current[r.vital] ?? []), r.value];
        current[r.vital] = series.slice(-SPARKLINE_POINTS);
      }
      return { ...all, [m.patientId]: current };
    });
  }
}
