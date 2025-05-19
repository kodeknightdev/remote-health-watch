import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AlertDetail,
  AlertNote,
  AlertSummary,
  CareTeamMember,
  Device,
  Kpis,
  PatientDetail,
  PatientOverview,
  PatientSummary,
  ReportRef,
  RiskScore,
  Threshold,
  TrendResponse,
  User,
  VitalType,
} from './models';

export interface EnrollRequest {
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  sex: string;
  conditions: string[];
  plan: { vitals: VitalType[]; intervalSeconds: number };
  careTeam: CareTeamMember[];
  onSupplementalOxygen: boolean;
  deviceSerial?: string | null;
}

@Injectable({ providedIn: 'root' })
export class PatientsApi {
  private readonly http = inject(HttpClient);

  list(status?: string): Observable<PatientSummary[]> {
    const params = status ? new HttpParams().set('status', status) : undefined;
    return this.http.get<PatientSummary[]>('/api/patients', { params });
  }

  get(id: string): Observable<PatientDetail> {
    return this.http.get<PatientDetail>(`/api/patients/${id}`);
  }

  overview(id: string): Observable<PatientOverview> {
    return this.http.get<PatientOverview>(`/api/patients/${id}/overview`);
  }

  enroll(request: EnrollRequest): Observable<PatientDetail> {
    return this.http.post<PatientDetail>('/api/patients', request);
  }

  updateThresholds(id: string, thresholds: Threshold[]): Observable<Threshold[]> {
    return this.http.put<Threshold[]>(`/api/patients/${id}/thresholds`, { thresholds });
  }

  resetThreshold(id: string, metric: string): Observable<void> {
    return this.http.delete<void>(`/api/patients/${id}/thresholds/${metric}`);
  }

  discharge(id: string): Observable<void> {
    return this.http.post<void>(`/api/patients/${id}/discharge`, {});
  }

  staff(role?: string): Observable<User[]> {
    const params = role ? new HttpParams().set('role', role) : undefined;
    return this.http.get<User[]>('/api/users', { params });
  }
}

@Injectable({ providedIn: 'root' })
export class VitalsApi {
  private readonly http = inject(HttpClient);

  trends(patientId: string, vital: VitalType, range: '24h' | '7d' | '30d'): Observable<TrendResponse> {
    const params = new HttpParams().set('vital', vital).set('range', range);
    return this.http.get<TrendResponse>(`/api/vitals/${patientId}/trends`, { params });
  }
}

@Injectable({ providedIn: 'root' })
export class RiskApi {
  private readonly http = inject(HttpClient);

  scores(): Observable<RiskScore[]> {
    return this.http.get<RiskScore[]>('/api/risk/scores');
  }
}

@Injectable({ providedIn: 'root' })
export class AlertsApi {
  private readonly http = inject(HttpClient);

  inbox(status = 'open'): Observable<AlertSummary[]> {
    return this.http.get<AlertSummary[]>('/api/alerts', { params: new HttpParams().set('status', status) });
  }

  get(id: string): Observable<AlertDetail> {
    return this.http.get<AlertDetail>(`/api/alerts/${id}`);
  }

  acknowledge(id: string): Observable<AlertDetail> {
    return this.http.post<AlertDetail>(`/api/alerts/${id}/acknowledge`, {});
  }

  resolve(id: string, reason: string): Observable<AlertDetail> {
    return this.http.post<AlertDetail>(`/api/alerts/${id}/resolve`, { reason });
  }

  addNote(id: string, text: string): Observable<AlertNote> {
    return this.http.post<AlertNote>(`/api/alerts/${id}/notes`, { text });
  }
}

@Injectable({ providedIn: 'root' })
export class DevicesApi {
  private readonly http = inject(HttpClient);

  list(): Observable<Device[]> {
    return this.http.get<Device[]>('/api/devices');
  }

  register(serial: string, type: string, firmware: string): Observable<{ device: Device; apiKey: string }> {
    return this.http.post<{ device: Device; apiKey: string }>('/api/devices', { serial, type, firmware });
  }

  pair(id: string, patientId: string, expectedIntervalSeconds?: number): Observable<Device> {
    return this.http.post<Device>(`/api/devices/${id}/pair`, { patientId, expectedIntervalSeconds });
  }

  unpair(id: string): Observable<Device> {
    return this.http.post<Device>(`/api/devices/${id}/unpair`, {});
  }

  rotateKey(id: string): Observable<{ device: Device; apiKey: string }> {
    return this.http.post<{ device: Device; apiKey: string }>(`/api/devices/${id}/rotate-key`, {});
  }

  revoke(id: string): Observable<Device> {
    return this.http.post<Device>(`/api/devices/${id}/revoke`, {});
  }
}

@Injectable({ providedIn: 'root' })
export class InsightsApi {
  private readonly http = inject(HttpClient);

  kpis(days = 14): Observable<Kpis> {
    return this.http.get<Kpis>('/api/insights/kpis', { params: new HttpParams().set('days', days) });
  }

  reports(patientId: string): Observable<ReportRef[]> {
    return this.http.get<ReportRef[]>(`/api/insights/reports/patient/${patientId}`);
  }

  generate(patientId: string): Observable<ReportRef> {
    return this.http.post<ReportRef>(`/api/insights/reports/patient/${patientId}`, {});
  }

  download(reportId: string): Observable<Blob> {
    return this.http.get(`/api/insights/reports/${reportId}/pdf`, { responseType: 'blob' });
  }
}
