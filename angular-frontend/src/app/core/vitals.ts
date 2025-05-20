import { News2Band, VitalType } from './models';

export const VITAL_LABEL: Record<VitalType, string> = {
  HeartRate: 'HR',
  SpO2: 'SpO₂',
  RespiratoryRate: 'RR',
  Temperature: 'Temp',
  BloodPressure: 'BP',
  Glucose: 'Glucose',
};

export const VITAL_UNIT: Record<VitalType, string> = {
  HeartRate: 'bpm',
  SpO2: '%',
  RespiratoryRate: '/min',
  Temperature: '°C',
  BloodPressure: 'mmHg',
  Glucose: 'mg/dL',
};

export function formatVital(vital: VitalType, value: number, value2?: number | null): string {
  if (vital === 'BloodPressure') return `${Math.round(value)}/${value2 != null ? Math.round(value2) : '–'}`;
  if (vital === 'Temperature') return value.toFixed(1);
  return Math.round(value).toString();
}

export const BAND_ORDER: Record<News2Band, number> = { Low: 0, LowMedium: 1, Medium: 2, High: 3 };

export function bandLabel(band: News2Band): string {
  return band === 'LowMedium' ? 'Low–medium' : band;
}
