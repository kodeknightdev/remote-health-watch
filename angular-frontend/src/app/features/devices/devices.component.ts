import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { DevicesApi } from '../../core/api.service';
import { Device } from '../../core/models';

@Component({
  selector: 'rhw-devices',
  imports: [DatePipe, FormsModule, MatTableModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Device registry</h2>
    <div class="register">
      <mat-form-field subscriptSizing="dynamic"><mat-label>Serial</mat-label><input matInput [(ngModel)]="serial" /></mat-form-field>
      <mat-form-field subscriptSizing="dynamic">
        <mat-label>Type</mat-label>
        <mat-select [(ngModel)]="type">
          @for (t of types; track t) { <mat-option [value]="t">{{ t }}</mat-option> }
        </mat-select>
      </mat-form-field>
      <mat-form-field subscriptSizing="dynamic"><mat-label>Firmware</mat-label><input matInput [(ngModel)]="firmware" /></mat-form-field>
      <button mat-flat-button color="primary" (click)="register()" [disabled]="!serial">Register</button>
    </div>
    @if (issuedKey()) {
      <p class="key">Device key (shown once): <code>{{ issuedKey() }}</code></p>
    }
    <table mat-table [dataSource]="devices()" class="mat-elevation-z1">
      <ng-container matColumnDef="serial"><th mat-header-cell *matHeaderCellDef>Serial</th><td mat-cell *matCellDef="let d">{{ d.serial }}</td></ng-container>
      <ng-container matColumnDef="type"><th mat-header-cell *matHeaderCellDef>Type</th><td mat-cell *matCellDef="let d">{{ d.type }} · {{ d.firmware }}</td></ng-container>
      <ng-container matColumnDef="status">
        <th mat-header-cell *matHeaderCellDef>Status</th>
        <td mat-cell *matCellDef="let d">{{ d.status }} @if (d.silentSince) { <span class="silent">silent</span> }</td>
      </ng-container>
      <ng-container matColumnDef="battery"><th mat-header-cell *matHeaderCellDef>Battery</th><td mat-cell *matCellDef="let d">{{ d.batteryPercent ?? '–' }}%</td></ng-container>
      <ng-container matColumnDef="seen">
        <th mat-header-cell *matHeaderCellDef>Last seen</th>
        <td mat-cell *matCellDef="let d">{{ d.lastSeenAt ? (d.lastSeenAt | date: 'dd MMM HH:mm:ss') : 'never' }}</td>
      </ng-container>
      <ng-container matColumnDef="key"><th mat-header-cell *matHeaderCellDef>Key</th><td mat-cell *matCellDef="let d"><code>{{ d.keyPrefix }}…</code></td></ng-container>
      <ng-container matColumnDef="actions">
        <th mat-header-cell *matHeaderCellDef></th>
        <td mat-cell *matCellDef="let d">
          @if (d.status === 'Available') {
            <input class="pid" placeholder="patient id" #pid /><button mat-button (click)="pair(d, pid.value)">Pair</button>
          }
          @if (d.status === 'Paired') { <button mat-button (click)="unpair(d)">Unpair</button> }
          @if (d.status !== 'Revoked') {
            <button mat-button (click)="rotate(d)">Rotate key</button>
            <button mat-button color="warn" (click)="revoke(d)">Revoke</button>
          }
        </td>
      </ng-container>
      <tr mat-header-row *matHeaderRowDef="columns"></tr>
      <tr mat-row *matRowDef="let d; columns: columns"></tr>
    </table>
  `,
  styles: `
    .register { display: flex; gap: 12px; align-items: center; margin-bottom: 12px; }
    .key { background: #fff8e1; padding: 8px; border-radius: 4px; }
    table { width: 100%; }
    .silent { color: #c62828; font-size: 12px; margin-left: 4px; }
    .pid { width: 120px; }
  `,
})
export class DevicesComponent implements OnInit {
  private readonly api = inject(DevicesApi);
  private readonly snack = inject(MatSnackBar);
  readonly columns = ['serial', 'type', 'status', 'battery', 'seen', 'key', 'actions'];
  readonly types = ['MultiParameterPatch', 'BloodPressureCuff', 'PulseOximeter', 'Glucometer', 'Thermometer'];
  readonly devices = signal<Device[]>([]);
  readonly issuedKey = signal<string | null>(null);
  serial = '';
  type = 'MultiParameterPatch';
  firmware = '2.4.1';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.api.list().subscribe((d) => this.devices.set(d));
  }

  register(): void {
    this.api.register(this.serial, this.type, this.firmware).subscribe({
      next: (r) => {
        this.issuedKey.set(r.apiKey);
        this.serial = '';
        this.load();
      },
      error: () => this.snack.open('Registration failed', 'OK', { duration: 3000 }),
    });
  }

  pair(d: Device, patientId: string): void {
    this.api.pair(d.id, patientId.trim()).subscribe({
      next: () => this.load(),
      error: (e) => this.snack.open(e.error?.title ?? 'Pairing failed', 'OK', { duration: 4000 }),
    });
  }

  unpair(d: Device): void {
    this.api.unpair(d.id).subscribe(() => this.load());
  }

  rotate(d: Device): void {
    this.api.rotateKey(d.id).subscribe((r) => {
      this.issuedKey.set(r.apiKey);
      this.load();
    });
  }

  revoke(d: Device): void {
    this.api.revoke(d.id).subscribe(() => this.load());
  }
}
