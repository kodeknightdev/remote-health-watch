import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatChipsModule } from '@angular/material/chips';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { forkJoin } from 'rxjs';
import { PatientsApi, RiskApi } from '../../core/api.service';
import { LiveService } from '../../core/live.service';
import { PatientSummary, VitalType } from '../../core/models';
import { BAND_ORDER } from '../../core/vitals';
import { News2BadgeComponent } from '../../shared/news2-badge.component';
import { VitalTileComponent } from '../../shared/vital-tile.component';

export interface PatientRow extends PatientSummary {
  score: number | null;
}

/** Highest NEWS2 first, then band (a single red parameter outranks the same total), then name. */
export function sortByRisk(rows: PatientRow[], bands: Record<string, string | undefined>): PatientRow[] {
  return [...rows].sort((a, b) => {
    const s = (b.score ?? -1) - (a.score ?? -1);
    if (s !== 0) return s;
    const band = (BAND_ORDER[(bands[b.id] ?? 'Low') as keyof typeof BAND_ORDER] ?? 0) -
      (BAND_ORDER[(bands[a.id] ?? 'Low') as keyof typeof BAND_ORDER] ?? 0);
    return band !== 0 ? band : a.name.localeCompare(b.name);
  });
}

@Component({
  selector: 'rhw-patient-list',
  imports: [MatTableModule, MatFormFieldModule, MatInputModule, MatChipsModule, MatProgressBarModule, News2BadgeComponent, VitalTileComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <h2>Patients <small>{{ rows().length }}</small></h2>
      <mat-form-field appearance="outline" class="filter" subscriptSizing="dynamic">
        <mat-label>Filter</mat-label>
        <input matInput (input)="filter.set($any($event.target).value)" placeholder="name, MRN or condition" />
      </mat-form-field>
    </div>
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" />
    }
    <table mat-table [dataSource]="rows()" class="mat-elevation-z1">
      <ng-container matColumnDef="risk">
        <th mat-header-cell *matHeaderCellDef>NEWS2</th>
        <td mat-cell *matCellDef="let p"><rhw-news2-badge [score]="p.score" [band]="band(p.id)" /></td>
      </ng-container>
      <ng-container matColumnDef="patient">
        <th mat-header-cell *matHeaderCellDef>Patient</th>
        <td mat-cell *matCellDef="let p">
          <div class="name">{{ p.name }}</div>
          <div class="meta">{{ p.mrn }} · {{ p.age }}{{ p.sex }} · {{ p.status }}</div>
        </td>
      </ng-container>
      <ng-container matColumnDef="conditions">
        <th mat-header-cell *matHeaderCellDef>Conditions</th>
        <td mat-cell *matCellDef="let p">
          <mat-chip-set>
            @for (c of p.conditions; track c) {
              <mat-chip>{{ c }}</mat-chip>
            }
          </mat-chip-set>
        </td>
      </ng-container>
      <ng-container matColumnDef="vitals">
        <th mat-header-cell *matHeaderCellDef>Live vitals</th>
        <td mat-cell *matCellDef="let p">
          <div class="tiles">
            @for (v of tileVitals; track v) {
              <rhw-vital-tile [vital]="v" [latest]="live.latest()[p.id]?.[v]" [history]="live.history()[p.id]?.[v] ?? []" />
            }
          </div>
        </td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns; sticky: true"></tr>
      <tr mat-row *matRowDef="let p; columns: columns" class="row" (click)="open(p.id)"></tr>
    </table>
  `,
  styles: `
    .head { display: flex; align-items: center; justify-content: space-between; }
    h2 small { color: #78909c; font-weight: 400; }
    .filter { width: 320px; }
    table { width: 100%; }
    .row { cursor: pointer; }
    .row:hover { background: #f0f4ff; }
    .name { font-weight: 500; }
    .meta { font-size: 12px; color: #78909c; }
    .tiles { display: flex; gap: 6px; padding: 6px 0; }
  `,
})
export class PatientListComponent implements OnInit {
  private readonly patients = inject(PatientsApi);
  private readonly riskApi = inject(RiskApi);
  private readonly router = inject(Router);
  readonly live = inject(LiveService);

  readonly columns = ['risk', 'patient', 'conditions', 'vitals'];
  readonly tileVitals: VitalType[] = ['HeartRate', 'SpO2', 'RespiratoryRate', 'BloodPressure', 'Temperature'];
  readonly loading = signal(true);
  readonly filter = signal('');
  private readonly list = signal<PatientSummary[]>([]);

  readonly rows = computed(() => {
    const risk = this.live.risk();
    const f = this.filter().trim().toLowerCase();
    const rows = this.list()
      .filter((p) => !f || p.name.toLowerCase().includes(f) || p.mrn.toLowerCase().includes(f)
        || p.conditions.some((c) => c.toLowerCase().includes(f)))
      .map((p) => ({ ...p, score: risk[p.id]?.score ?? null }));
    const bands = Object.fromEntries(Object.entries(risk).map(([id, r]) => [id, r?.band]));
    return sortByRisk(rows, bands);
  });

  ngOnInit(): void {
    forkJoin({ patients: this.patients.list(), scores: this.riskApi.scores() }).subscribe({
      next: ({ patients, scores }) => {
        this.live.seedRisk(scores);
        this.list.set(patients);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  band(id: string) {
    return this.live.risk()[id]?.band ?? 'Low';
  }

  open(id: string): void {
    this.router.navigate(['/patients', id]);
  }
}
