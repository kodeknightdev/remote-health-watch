import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatButtonModule } from '@angular/material/button';
import { debounceTime } from 'rxjs';
import { AlertsApi } from '../../core/api.service';
import { LiveService } from '../../core/live.service';
import { AlertSummary } from '../../core/models';

export const ESCALATION_STEPS = ['Nurse', 'Doctor', 'Coordinator'];

@Component({
  selector: 'rhw-alert-inbox',
  imports: [DatePipe, RouterLink, MatButtonToggleModule, MatButtonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <h2>Alert inbox</h2>
      <mat-button-toggle-group [value]="status()" (change)="status.set($event.value); load()">
        <mat-button-toggle value="unacknowledged">Unacknowledged</mat-button-toggle>
        <mat-button-toggle value="open">Open</mat-button-toggle>
        <mat-button-toggle value="resolved">Resolved</mat-button-toggle>
      </mat-button-toggle-group>
    </div>
    @for (a of alerts(); track a.id) {
      <div class="alert" [class]="'sev-' + a.severity">
        <div class="main">
          <a [routerLink]="['/alerts', a.id]" class="msg"
            ><strong>{{ a.severity }}</strong> · {{ a.type }} · {{ a.message }}</a
          >
          <div class="meta">
            raised {{ a.raisedAt | date: 'dd MMM HH:mm:ss' }}
            @if (a.occurrenceCount > 1) {
              · repeated {{ a.occurrenceCount }}×
            }
            · <a [routerLink]="['/patients', a.patientId]">patient</a>
          </div>
          <div class="steps">
            @for (s of steps; track s; let i = $index) {
              <span class="step" [class.done]="a.escalationLevel >= i && a.severity !== 'Info'" [class.current]="a.escalationLevel === i">{{
                s
              }}</span>
            }
            <span class="status">{{ a.status }}</span>
            @if (a.nextEscalationAt && !a.acknowledgedAt) {
              <span class="next">next escalation {{ a.nextEscalationAt | date: 'HH:mm:ss' }}</span>
            }
          </div>
        </div>
        @if (a.status !== 'Acknowledged' && a.status !== 'Resolved') {
          <button mat-flat-button color="primary" (click)="ack(a)">Acknowledge</button>
        }
      </div>
    } @empty {
      <p class="muted">Nothing here.</p>
    }
  `,
  styles: `
    .head {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
    .alert {
      display: flex;
      justify-content: space-between;
      align-items: center;
      gap: 12px;
      background: #fff;
      margin: 8px 0;
      padding: 10px 12px;
      border-left: 5px solid #90a4ae;
      border-radius: 4px;
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
    .sev-Low {
      border-color: #1e88e5;
    }
    .msg {
      color: inherit;
      text-decoration: none;
    }
    .meta,
    .muted,
    .next,
    .status {
      color: #78909c;
      font-size: 12px;
    }
    .steps {
      display: flex;
      gap: 6px;
      align-items: center;
      margin-top: 4px;
    }
    .step {
      font-size: 11px;
      padding: 1px 6px;
      border-radius: 8px;
      background: #eceff1;
      color: #90a4ae;
    }
    .step.done {
      background: #ffe0b2;
      color: #8a3d00;
    }
    .step.current {
      outline: 1px solid #ef6c00;
    }
  `,
})
export class AlertInboxComponent implements OnInit {
  private readonly api = inject(AlertsApi);
  private readonly live = inject(LiveService);
  private readonly destroyRef = inject(DestroyRef);
  readonly steps = ESCALATION_STEPS;
  readonly status = signal('unacknowledged');
  readonly alerts = signal<AlertSummary[]>([]);

  ngOnInit(): void {
    this.load();
    // any lifecycle push reloads the list; debounced so an alert storm is one request
    this.live.alerts$.pipe(debounceTime(500), takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
  }

  load(): void {
    this.api.inbox(this.status()).subscribe((a) => this.alerts.set(a));
  }

  ack(a: AlertSummary): void {
    this.api.acknowledge(a.id).subscribe({ next: () => this.load(), error: () => this.load() });
  }
}
