import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { PatientsApi } from '../../core/api.service';
import { CONDITIONS, CareTeamMember, User, VITALS } from '../../core/models';

@Component({
  selector: 'rhw-enroll',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatCheckboxModule, MatButtonModule, MatCardModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-card>
      <mat-card-header><mat-card-title>Enroll a patient</mat-card-title></mat-card-header>
      <mat-card-content>
        <form [formGroup]="form" (ngSubmit)="submit()" class="grid">
          <mat-form-field><mat-label>First name</mat-label><input matInput formControlName="firstName" /></mat-form-field>
          <mat-form-field><mat-label>Last name</mat-label><input matInput formControlName="lastName" /></mat-form-field>
          <mat-form-field><mat-label>Date of birth</mat-label><input matInput type="date" formControlName="dateOfBirth" /></mat-form-field>
          <mat-form-field>
            <mat-label>Sex</mat-label>
            <mat-select formControlName="sex"><mat-option value="F">F</mat-option><mat-option value="M">M</mat-option><mat-option value="X">X</mat-option></mat-select>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Conditions</mat-label>
            <mat-select formControlName="conditions" multiple>
              @for (c of conditions; track c) { <mat-option [value]="c">{{ c }}</mat-option> }
            </mat-select>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Monitored vitals</mat-label>
            <mat-select formControlName="vitals" multiple>
              @for (v of vitals; track v) { <mat-option [value]="v">{{ v }}</mat-option> }
            </mat-select>
          </mat-form-field>
          <mat-form-field><mat-label>Interval (seconds)</mat-label><input matInput type="number" formControlName="intervalSeconds" /></mat-form-field>
          <mat-form-field><mat-label>Device serial (optional)</mat-label><input matInput formControlName="deviceSerial" /></mat-form-field>
          <mat-form-field>
            <mat-label>Nurse</mat-label>
            <mat-select formControlName="nurseId">
              @for (u of nurses(); track u.id) { <mat-option [value]="u.id">{{ u.displayName }}</mat-option> }
            </mat-select>
          </mat-form-field>
          <mat-form-field>
            <mat-label>Doctor</mat-label>
            <mat-select formControlName="doctorId">
              @for (u of doctors(); track u.id) { <mat-option [value]="u.id">{{ u.displayName }}</mat-option> }
            </mat-select>
          </mat-form-field>
          <mat-checkbox formControlName="onSupplementalOxygen">On supplemental oxygen</mat-checkbox>
          <div class="actions">
            @if (error()) { <span class="error">{{ error() }}</span> }
            <button mat-flat-button color="primary" type="submit" [disabled]="form.invalid || busy()">Enroll</button>
          </div>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `
    .grid { display: grid; grid-template-columns: repeat(2, minmax(220px, 1fr)); gap: 0 16px; margin-top: 12px; }
    .actions { grid-column: 1 / -1; display: flex; justify-content: flex-end; gap: 12px; align-items: center; }
    .error { color: #c62828; }
  `,
})
export class EnrollComponent implements OnInit {
  private readonly api = inject(PatientsApi);
  private readonly router = inject(Router);
  readonly conditions = CONDITIONS;
  readonly vitals = VITALS;
  readonly nurses = signal<User[]>([]);
  readonly doctors = signal<User[]>([]);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = inject(FormBuilder).nonNullable.group({
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    dateOfBirth: ['', Validators.required],
    sex: ['F', Validators.required],
    conditions: [[] as string[]],
    vitals: [['HeartRate', 'SpO2', 'RespiratoryRate', 'Temperature', 'BloodPressure'] as string[], Validators.required],
    intervalSeconds: [60, [Validators.required, Validators.min(5), Validators.max(3600)]],
    deviceSerial: [''],
    nurseId: ['', Validators.required],
    doctorId: [''],
    onSupplementalOxygen: [false],
  });

  ngOnInit(): void {
    this.api.staff('Nurse').subscribe((u) => this.nurses.set(u));
    this.api.staff('Doctor').subscribe((u) => this.doctors.set(u));
  }

  submit(): void {
    const v = this.form.getRawValue();
    const member = (id: string, role: 'Nurse' | 'Doctor', list: User[]): CareTeamMember[] => {
      const u = list.find((x) => x.id === id);
      return u ? [{ userId: u.id, role, displayName: u.displayName, email: u.email, isPrimary: true }] : [];
    };
    this.busy.set(true);
    this.api
      .enroll({
        firstName: v.firstName,
        lastName: v.lastName,
        dateOfBirth: v.dateOfBirth,
        sex: v.sex,
        conditions: v.conditions,
        plan: { vitals: v.vitals as never, intervalSeconds: v.intervalSeconds },
        careTeam: [...member(v.nurseId, 'Nurse', this.nurses()), ...member(v.doctorId, 'Doctor', this.doctors())],
        onSupplementalOxygen: v.onSupplementalOxygen,
        deviceSerial: v.deviceSerial || null,
      })
      .subscribe({
        next: (p) => this.router.navigate(['/patients', p.id]),
        error: () => {
          this.busy.set(false);
          this.error.set('Enrollment failed, check the form');
        },
      });
  }
}
