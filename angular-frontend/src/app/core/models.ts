export type Role = 'Doctor' | 'Nurse' | 'CareCoordinator' | 'Admin';
export type VitalType = 'HeartRate' | 'SpO2' | 'RespiratoryRate' | 'Temperature' | 'BloodPressure' | 'Glucose';
export type News2Band = 'Low' | 'LowMedium' | 'Medium' | 'High';
export type MonitoringStatus = 'PendingDevice' | 'BaselineBuilding' | 'MonitoringActive' | 'Discharged';
export type AlertSeverity = 'Info' | 'Low' | 'Medium' | 'High' | 'Critical';
export type AlertStatus = 'Raised' | 'NotifiedNurse' | 'EscalatedDoctor' | 'EscalatedCoordinator' | 'Acknowledged' | 'Resolved';
export type AlertType =
  | 'ThresholdBreach' | 'News2BandChange' | 'Anomaly' | 'DeteriorationTrend' | 'DeviceSilent' | 'DeviceUnpaired' | 'PairingFailed';

export const VITALS: VitalType[] = ['HeartRate', 'SpO2', 'RespiratoryRate', 'Temperature', 'BloodPressure', 'Glucose'];
export const CONDITIONS = ['COPD', 'Hypertension', 'HeartFailure', 'Diabetes', 'PostSurgical', 'Pneumonia'];

export interface User {
  id: string;
  email: string;
  displayName: string;
  role: Role;
  isOnCall: boolean;
}

export interface TokenResponse {
  accessToken: string;
  expiresAt: string;
  user: User;
}

export interface PatientSummary {
  id: string;
  mrn: string;
  name: string;
  age: number;
  sex: string;
  conditions: string[];
  status: MonitoringStatus;
  enrolledAt: string;
  primaryNurse?: string;
  primaryDoctor?: string;
}

export interface CareTeamMember {
  userId: string;
  role: Role;
  displayName: string;
  email: string;
  phone?: string;
  isPrimary: boolean;
}

export interface Threshold {
  metric: string;
  lowCritical?: number | null;
  lowWarning?: number | null;
  highWarning?: number | null;
  highCritical?: number | null;
  source?: 'Default' | 'Condition' | 'Override';
}

export interface PatientDetail {
  id: string;
  mrn: string;
  firstName: string;
  lastName: string;
  dateOfBirth: string;
  age: number;
  sex: string;
  conditions: string[];
  status: MonitoringStatus;
  plan: { vitals: VitalType[]; intervalSeconds: number };
  spO2Scale: number;
  onSupplementalOxygen: boolean;
  pairedDeviceCount: number;
  lastPairingError?: string;
  enrolledAt: string;
  baselineStartedAt?: string;
  monitoringActiveAt?: string;
  dischargedAt?: string;
  careTeam: CareTeamMember[];
  thresholds: Threshold[];
}

export interface LatestValue {
  vital: VitalType;
  value: number;
  value2?: number | null;
  unit: string;
  measuredAt: string;
}

export interface TrendPoint {
  t: string;
  avg: number;
  min: number;
  max: number;
  avg2?: number | null;
  count: number;
}

export interface TrendResponse {
  vital: VitalType;
  range: string;
  grain: string;
  unit: string;
  points: TrendPoint[];
}

export interface VitalSummary {
  vital: VitalType;
  count: number;
  min: number;
  max: number;
  avg: number;
  avg2?: number | null;
  unit: string;
}

export interface RiskScore {
  patientId: string;
  score: number;
  band: News2Band;
  components: Record<string, number>;
  complete?: boolean;
  computedAt: string;
}

export interface AlertSummary {
  id: string;
  patientId: string;
  type: AlertType;
  severity: AlertSeverity;
  vital?: VitalType;
  metric?: string;
  value?: number;
  message: string;
  status: AlertStatus;
  escalationLevel: number;
  assignedRole: string;
  raisedAt: string;
  lastOccurredAt: string;
  occurrenceCount: number;
  nextEscalationAt?: string;
  acknowledgedAt?: string;
  resolvedAt?: string;
}

export interface TimelineEntry {
  id: number;
  at: string;
  kind: string;
  actorUserId?: string;
  detail: string;
}

export interface AlertNote {
  id: string;
  authorName: string;
  text: string;
  at: string;
}

export interface AlertDetail {
  alert: AlertSummary;
  timeline: TimelineEntry[];
  notes: AlertNote[];
  resolutionReason?: string;
}

export interface Device {
  id: string;
  serial: string;
  type: string;
  firmware: string;
  status: 'Available' | 'Paired' | 'Revoked';
  patientId?: string;
  pairedAt?: string;
  expectedIntervalSeconds: number;
  keyPrefix: string;
  keyIssuedAt: string;
  batteryPercent?: number;
  lastSeenAt?: string;
  silentSince?: string;
}

export interface PatientOverview {
  patientId: string;
  patient: PatientDetail | null;
  latestVitals: LatestValue[] | null;
  summary24h: { vitals: VitalSummary[]; lastReadingAt?: string } | null;
  openAlerts: AlertSummary[] | null;
  risk: RiskScore | null;
  devices: Device[] | null;
  degraded: string[];
}

export interface Kpis {
  enrolledPatients: number;
  activePatients: number;
  alertsPerDay: { day: string; count: number }[];
  averageMinutesToAcknowledge: number | null;
  escalationRate: number;
  wearTimeCompliance: number | null;
  generatedAt: string;
}

export interface ReportRef {
  reportId: string;
  patientId: string;
  weekStart: string;
  blobName: string;
  generatedAt: string;
}

export interface AlertPush {
  kind: 'raised' | 'acknowledged' | 'escalated' | 'resolved';
  alertId: string;
  patientId: string;
  severity?: AlertSeverity;
  type?: AlertType;
  status?: AlertStatus;
  message?: string;
  occurredAt: string;
  pushedAt: string;
}

export interface VitalsPush {
  patientId: string;
  readings: LatestValue[];
}
