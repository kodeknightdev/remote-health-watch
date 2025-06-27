import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatButtonModule } from '@angular/material/button';
import { MatTabsModule } from '@angular/material/tabs';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar } from '@angular/material/snack-bar';
import { InsightsApi, PatientsApi, VitalsApi } from '../../core/api.service';
import { AuthService } from '../../core/auth.service';
import { LiveService } from '../../core/live.service';
import { PatientOverview, ReportRef, Threshold, TrendResponse, VITALS, VitalType } from '../../core/models';
import { VITAL_LABEL } from '../../core/vitals';
import { News2BadgeComponent } from '../../shared/news2-badge.component';
import { TrendChartComponent } from '../../shared/trend-chart.component';
import { VitalTileComponent } from '../../shared/vital-tile.component';

@Component({
  selector: 'rhw-patient-detail',
  imports: [
    DatePipe,
    DecimalPipe,
    FormsModule,
    RouterLink,
    MatCardModule,
    MatButtonToggleModule,
    MatButtonModule,
    MatTabsModule,
    MatInputModule,
    News2BadgeComponent,
    TrendChartComponent,
    VitalTileComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (overview(); as o) {
      @if (o.patient; as p) {
        <div class="head">
          <div>
            <h2>{{ p.firstName }} {{ p.lastName }}</h2>
            <div class="meta">
              {{ p.mrn }} · {{ p.age }}{{ p.sex }} · {{ p.conditions.join(', ') || 'no conditions' }} · {{ p.status }}
              @if (p.spO2Scale === 2) {
                · SpO₂ scale 2
              }
              @if (p.onSupplementalOxygen) {
                · on O₂
              }
            </div>
          </div>
          <rhw-news2-badge [score]="risk()?.score ?? null" [band]="risk()?.band ?? 'Low'" />
        </div>
        @if (o.degraded.length) {
          <p class="degraded">Some sections are unavailable right now: {{ o.degraded.join(', ') }}</p>
        }
        <div class="tiles">
          @for (v of vitals; track v) {
            <rhw-vital-tile [vital]="v" [latest]="live.latest()[p.id]?.[v]" [history]="live.history()[p.id]?.[v] ?? []" />
          }
        </div>

        <mat-tab-group animationDuration="0ms">
          <mat-tab label="Trends">
            <div class="toolbar">
              <mat-button-toggle-group [value]="vital()" (change)="vital.set($event.value); loadTrend()">
                @for (v of vitals; track v) {
                  <mat-button-toggle [value]="v">{{ label(v) }}</mat-button-toggle>
                }
              </mat-button-toggle-group>
              <mat-button-toggle-group [value]="range()" (change)="range.set($event.value); loadTrend()">
                <mat-button-toggle value="24h">24h</mat-button-toggle>
                <mat-button-toggle value="7d">7d</mat-button-toggle>
                <mat-button-toggle value="30d">30d</mat-button-toggle>
              </mat-button-toggle-group>
            </div>
            <rhw-trend-chart [trend]="trend()" [thresholds]="thresholds()" />
            @if (o.summary24h?.vitals?.length) {
              <table class="summary">
                <tr>
                  <th>24h</th>
                  <th>min</th>
                  <th>avg</th>
                  <th>max</th>
                  <th>readings</th>
                </tr>
                @for (s of o.summary24h!.vitals; track s.vital) {
                  <tr>
                    <td>{{ label(s.vital) }}</td>
                    <td>{{ s.min | number: '1.0-1' }}</td>
                    <td>{{ s.avg | number: '1.0-1' }}</td>
                    <td>{{ s.max | number: '1.0-1' }}</td>
                    <td>{{ s.count }}</td>
                  </tr>
                }
              </table>
            }
          </mat-tab>

          <mat-tab label="Alerts ({{ o.openAlerts?.length ?? 0 }})">
            @for (a of o.openAlerts ?? []; track a.id) {
              <a class="alert" [routerLink]="['/alerts', a.id]" [class]="'sev-' + a.severity">
                <strong>{{ a.severity }}</strong> {{ a.message }}
                <span class="when">{{ a.raisedAt | date: 'dd MMM HH:mm' }} · {{ a.status }}</span>
              </a>
            } @empty {
              <p class="muted">No open alerts.</p>
            }
          </mat-tab>

          <mat-tab label="Thresholds">
            <table class="thresholds">
              <tr>
                <th>Metric</th>
                <th>Low crit</th>
                <th>Low warn</th>
                <th>High warn</th>
                <th>High crit</th>
                <th>Source</th>
                <th></th>
              </tr>
              @for (t of thresholds(); track t.metric) {
                <tr>
                  <td>{{ t.metric }}</td>
                  <td><input type="number" [(ngModel)]="t.lowCritical" [disabled]="!canEdit()" /></td>
                  <td><input type="number" [(ngModel)]="t.lowWarning" [disabled]="!canEdit()" /></td>
                  <td><input type="number" [(ngModel)]="t.highWarning" [disabled]="!canEdit()" /></td>
                  <td><input type="number" [(ngModel)]="t.highCritical" [disabled]="!canEdit()" /></td>
                  <td>{{ t.source }}</td>
                  <td>
                    @if (canEdit() && t.source === 'Override') {
                      <button mat-button (click)="reset(t.metric)">Reset</button>
                    }
                  </td>
                </tr>
              }
            </table>
            @if (canEdit()) {
              <button mat-flat-button color="primary" (click)="saveThresholds()">Save thresholds</button>
            }
          </mat-tab>

          <mat-tab label="Devices">
            @for (d of o.devices ?? []; track d.id) {
              <div class="device">
                <strong>{{ d.serial }}</strong> {{ d.type }} · fw {{ d.firmware }} · {{ d.status }} · battery {{ d.batteryPercent ?? '–' }}%
                · last seen {{ d.lastSeenAt ? (d.lastSeenAt | date: 'dd MMM HH:mm:ss') : 'never' }}
                @if (d.silentSince) {
                  <span class="silent">silent since {{ d.silentSince | date: 'HH:mm' }}</span>
                }
              </div>
            } @empty {
              <p class="muted">No devices paired. {{ p.lastPairingError ? 'Last pairing error: ' + p.lastPairingError : '' }}</p>
            }
          </mat-tab>

          <mat-tab label="Care team">
            @for (m of p.careTeam; track m.userId) {
              <div>
                {{ m.displayName }} · {{ m.role }}
                @if (m.isPrimary) {
                  (primary)
                }
              </div>
            }
          </mat-tab>

          <mat-tab label="Reports">
            @if (canManage()) {
              <button mat-stroked-button (click)="generateReport()">Generate this week's report</button>
            }
            @for (r of reports(); track r.reportId) {
              <div class="report">Week of {{ r.weekStart }} · <button mat-button (click)="download(r)">Download PDF</button></div>
            } @empty {
              <p class="muted">No reports yet.</p>
            }
          </mat-tab>
        </mat-tab-group>
      }
    } @else if (error()) {
      <p class="degraded">{{ error() }}</p>
    }
  `,
  styles: `
    .head {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    h2 {
      margin: 0;
    }
    .meta,
    .muted,
    .when {
      color: #78909c;
      font-size: 13px;
    }
    .tiles {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
      margin: 12px 0;
    }
    .toolbar {
      display: flex;
      gap: 12px;
      margin: 12px 0;
      flex-wrap: wrap;
    }
    .summary,
    .thresholds {
      border-collapse: collapse;
      margin: 12px 0;
    }
    .summary td,
    .summary th,
    .thresholds td,
    .thresholds th {
      padding: 4px 10px;
      text-align: left;
      border-bottom: 1px solid #eceff1;
    }
    .thresholds input {
      width: 70px;
    }
    .alert {
      display: block;
      padding: 8px;
      margin: 6px 0;
      border-left: 4px solid #90a4ae;
      background: #fff;
      text-decoration: none;
      color: inherit;
    }
    .sev-Critical {
      border-color: #c62828;
    }
    .sev-High {
      border-color: #ef6c00;
    }
    .sev-Medium {
      border-color: #f9a825;
    }
    .degraded {
      background: #fff3e0;
      padding: 8px;
      border-radius: 4px;
    }
    .device {
      padding: 6px 0;
    }
    .silent {
      color: #c62828;
      margin-left: 8px;
    }
  `,
})
export class PatientDetailComponent implements OnInit {
  readonly id = input.required<string>();
  private readonly patientsApi = inject(PatientsApi);
  private readonly vitalsApi = inject(VitalsApi);
  private readonly insights = inject(InsightsApi);
  private readonly auth = inject(AuthService);
  private readonly snack = inject(MatSnackBar);
  readonly live = inject(LiveService);

  readonly vitals: VitalType[] = VITALS;
  readonly overview = signal<PatientOverview | null>(null);
  readonly error = signal<string | null>(null);
  readonly trend = signal<TrendResponse | null>(null);
  readonly thresholds = signal<Threshold[]>([]);
  readonly reports = signal<ReportRef[]>([]);
  readonly vital = signal<VitalType>('HeartRate');
  readonly range = signal<'24h' | '7d' | '30d'>('24h');
  readonly risk = computed(() => this.live.risk()[this.id()] ?? this.overview()?.risk ?? null);
  readonly canEdit = computed(() => this.auth.hasRole('Doctor', 'Nurse', 'CareCoordinator', 'Admin'));
  readonly canManage = computed(() => this.auth.hasRole('CareCoordinator', 'Admin'));

  ngOnInit(): void {
    this.live.watch(this.id());
    this.patientsApi.overview(this.id()).subscribe({
      next: (o) => {
        this.overview.set(o);
        this.thresholds.set((o.patient?.thresholds ?? []).map((t) => ({ ...t })));
        if (o.latestVitals) this.live.seedLatest(this.id(), o.latestVitals);
        if (o.risk) this.live.seedRisk([o.risk]);
      },
      error: (e) => this.error.set(e.status === 403 ? 'This patient is not on your care team.' : 'Could not load patient.'),
    });
    this.loadTrend();
    this.insights.reports(this.id()).subscribe({ next: (r) => this.reports.set(r), error: () => this.reports.set([]) });
  }

  label(v: VitalType): string {
    return VITAL_LABEL[v];
  }

  loadTrend(): void {
    this.vitalsApi.trends(this.id(), this.vital(), this.range()).subscribe((t) => this.trend.set(t));
  }

  saveThresholds(): void {
    const clean = this.thresholds().map((t) => ({
      metric: t.metric,
      lowCritical: t.lowCritical ?? null,
      lowWarning: t.lowWarning ?? null,
      highWarning: t.highWarning ?? null,
      highCritical: t.highCritical ?? null,
    }));
    this.patientsApi.updateThresholds(this.id(), clean).subscribe({
      next: (t) => {
        this.thresholds.set(t.map((x) => ({ ...x })));
        this.snack.open('Thresholds saved', undefined, { duration: 2500 });
      },
      error: (e) => this.snack.open(e.status === 403 ? 'Not allowed' : 'Thresholds are not valid', 'OK', { duration: 4000 }),
    });
  }

  reset(metric: string): void {
    this.patientsApi
      .resetThreshold(this.id(), metric)
      .subscribe(() => this.patientsApi.get(this.id()).subscribe((p) => this.thresholds.set(p.thresholds.map((t) => ({ ...t })))));
  }

  generateReport(): void {
    this.insights.generate(this.id()).subscribe((r) => this.reports.update((list) => [r, ...list]));
  }

  download(r: ReportRef): void {
    this.insights.download(r.reportId).subscribe((blob) => {
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `remote-health-watch-${r.weekStart}.pdf`;
      a.click();
      URL.revokeObjectURL(url);
    });
  }
}
