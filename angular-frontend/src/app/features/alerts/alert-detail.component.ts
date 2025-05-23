import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { AlertsApi } from '../../core/api.service';
import { AlertDetail } from '../../core/models';

@Component({
  selector: 'rhw-alert-detail',
  imports: [DatePipe, FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (detail(); as d) {
      <h2>{{ d.alert.severity }} · {{ d.alert.type }}</h2>
      <p>{{ d.alert.message }}</p>
      <p class="meta">Status {{ d.alert.status }} · assigned {{ d.alert.assignedRole }} ·
        <a [routerLink]="['/patients', d.alert.patientId]">open patient</a></p>
      <div class="actions">
        @if (d.alert.status !== 'Acknowledged' && d.alert.status !== 'Resolved') {
          <button mat-flat-button color="primary" (click)="ack()">Acknowledge</button>
        }
        @if (d.alert.status !== 'Resolved') {
          <mat-form-field subscriptSizing="dynamic"><mat-label>Resolution</mat-label><input matInput [(ngModel)]="reason" /></mat-form-field>
          <button mat-stroked-button (click)="resolve()" [disabled]="!reason">Resolve</button>
        }
      </div>
      <h3>Escalation timeline</h3>
      <ol class="timeline">
        @for (t of d.timeline; track t.id) {
          <li><span class="at">{{ t.at | date: 'dd MMM HH:mm:ss' }}</span> <strong>{{ t.kind }}</strong> {{ t.detail }}</li>
        }
      </ol>
      <h3>Notes</h3>
      @for (n of d.notes; track n.id) {
        <div class="note"><span class="at">{{ n.at | date: 'dd MMM HH:mm' }}</span> <strong>{{ n.authorName }}</strong>: {{ n.text }}</div>
      }
      <div class="actions">
        <mat-form-field class="grow" subscriptSizing="dynamic"><mat-label>Add a note</mat-label><input matInput [(ngModel)]="note" /></mat-form-field>
        <button mat-stroked-button (click)="addNote()" [disabled]="!note">Add</button>
      </div>
    }
  `,
  styles: `
    .meta, .at { color: #78909c; font-size: 12px; }
    .actions { display: flex; gap: 12px; align-items: center; margin: 12px 0; }
    .grow { flex: 1; }
    .timeline { border-left: 2px solid #cfd8dc; padding-left: 16px; list-style: none; }
    .timeline li { margin: 6px 0; }
    .note { margin: 4px 0; }
  `,
})
export class AlertDetailComponent implements OnInit {
  readonly id = input.required<string>();
  private readonly api = inject(AlertsApi);
  readonly detail = signal<AlertDetail | null>(null);
  reason = '';
  note = '';

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    this.api.get(this.id()).subscribe((d) => this.detail.set(d));
  }

  ack(): void {
    this.api.acknowledge(this.id()).subscribe({ next: (d) => this.detail.set(d), error: () => this.reload() });
  }

  resolve(): void {
    this.api.resolve(this.id(), this.reason).subscribe({ next: (d) => this.detail.set(d), error: () => this.reload() });
  }

  addNote(): void {
    this.api.addNote(this.id(), this.note).subscribe(() => {
      this.note = '';
      this.reload();
    });
  }
}
