import { Routes } from '@angular/router';
import { authGuard, roleGuard } from './core/auth.guard';
import { LoginComponent } from './features/login/login.component';
import { ShellComponent } from './features/shell/shell.component';

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'patients' },
      { path: 'patients', loadComponent: () => import('./features/patients/patient-list.component').then((m) => m.PatientListComponent) },
      {
        path: 'patients/:id',
        loadComponent: () => import('./features/patients/patient-detail.component').then((m) => m.PatientDetailComponent),
      },
      { path: 'alerts', loadComponent: () => import('./features/alerts/alert-inbox.component').then((m) => m.AlertInboxComponent) },
      { path: 'alerts/:id', loadComponent: () => import('./features/alerts/alert-detail.component').then((m) => m.AlertDetailComponent) },
      {
        path: 'enroll',
        canActivate: [roleGuard('CareCoordinator', 'Admin')],
        loadComponent: () => import('./features/patients/enroll.component').then((m) => m.EnrollComponent),
      },
      {
        path: 'devices',
        canActivate: [roleGuard('CareCoordinator', 'Admin')],
        loadComponent: () => import('./features/devices/devices.component').then((m) => m.DevicesComponent),
      },
      {
        path: 'programme',
        canActivate: [roleGuard('CareCoordinator', 'Admin')],
        loadComponent: () => import('./features/programme/programme.component').then((m) => m.ProgrammeComponent),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
