import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatBadgeModule } from '@angular/material/badge';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Subscription } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { LiveService } from '../../core/live.service';

@Component({
  selector: 'rhw-shell',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatSidenavModule, MatListModule, MatIconModule,
    MatButtonModule, MatBadgeModule, MatSnackBarModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-toolbar color="primary" class="bar">
      <span class="brand">remote-health-watch</span>
      <span class="demo">demo · synthetic data · not a medical device</span>
      <span class="spacer"></span>
      <span class="live" [class]="live.state()" [attr.title]="'Live connection ' + live.state()">● {{ live.state() }}</span>
      @if (live.alertLatencyMs() !== null) {
        <span class="latency" title="alert event to browser">{{ live.alertLatencyMs() }} ms</span>
      }
      <span class="user">{{ auth.user()?.displayName }} · {{ auth.role() }}</span>
      <button mat-icon-button (click)="auth.logout()" aria-label="Sign out"><mat-icon>logout</mat-icon></button>
    </mat-toolbar>
    <mat-sidenav-container class="container">
      <mat-sidenav mode="side" opened class="nav">
        <mat-nav-list>
          <a mat-list-item routerLink="/patients" routerLinkActive="active"><mat-icon matListItemIcon>monitor_heart</mat-icon>Patients</a>
          <a mat-list-item routerLink="/alerts" routerLinkActive="active">
            <mat-icon matListItemIcon [matBadge]="unseen() || null" matBadgeColor="warn" matBadgeSize="small">notifications</mat-icon>Alerts
          </a>
          @if (canManage()) {
            <a mat-list-item routerLink="/enroll" routerLinkActive="active"><mat-icon matListItemIcon>person_add</mat-icon>Enroll</a>
            <a mat-list-item routerLink="/devices" routerLinkActive="active"><mat-icon matListItemIcon>sensors</mat-icon>Devices</a>
            <a mat-list-item routerLink="/programme" routerLinkActive="active"><mat-icon matListItemIcon>insights</mat-icon>Programme</a>
          }
        </mat-nav-list>
      </mat-sidenav>
      <mat-sidenav-content class="content" (click)="unseen.set(0)">
        <router-outlet />
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: `
    .bar { position: sticky; top: 0; z-index: 2; gap: 12px; }
    .brand { font-weight: 600; }
    .demo { font-size: 12px; opacity: 0.8; }
    .spacer { flex: 1; }
    .live { font-size: 12px; }
    .live.connected { color: #b9f6ca; }
    .live.reconnecting, .live.connecting { color: #ffe57f; }
    .live.disconnected { color: #ff8a80; }
    .latency, .user { font-size: 13px; }
    .container { height: calc(100vh - 64px); }
    .nav { width: 200px; }
    .content { padding: 16px; background: #f5f7fa; }
    .active { background: rgba(21, 101, 192, 0.08); }
  `,
})
export class ShellComponent implements OnInit, OnDestroy {
  readonly auth = inject(AuthService);
  readonly live = inject(LiveService);
  private readonly snack = inject(MatSnackBar);
  readonly unseen = signal(0);
  readonly canManage = computed(() => this.auth.hasRole('CareCoordinator', 'Admin'));
  private sub?: Subscription;

  ngOnInit(): void {
    this.live.start();
    this.sub = this.live.alerts$.subscribe((a) => {
      if (a.kind !== 'raised' && a.kind !== 'escalated') return;
      this.unseen.update((n) => n + 1);
      if (a.severity === 'Critical' || a.severity === 'High') {
        this.snack.open(`${a.severity} alert ${a.kind}: ${a.message ?? a.status}`, 'Dismiss', { duration: 8000 });
      }
    });
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
    this.live.stop();
  }
}
