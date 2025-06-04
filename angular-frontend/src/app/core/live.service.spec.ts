import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { LiveService } from './live.service';

describe('LiveService store', () => {
  let live: LiveService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideRouter([])] });
    live = TestBed.inject(LiveService);
  });

  it('never lets an older reading replace a newer one', () => {
    live.seedLatest('p1', [{ vital: 'HeartRate', value: 80, unit: 'bpm', measuredAt: '2025-05-20T10:00:05Z' }]);
    live.seedLatest('p1', [{ vital: 'HeartRate', value: 120, unit: 'bpm', measuredAt: '2025-05-20T09:59:00Z' }]);
    expect(live.latest()['p1']?.HeartRate?.value).toBe(80);
  });

  it('keeps pushed risk scores over the initial snapshot', () => {
    live.risk.set({ p1: { patientId: 'p1', score: 7, band: 'High', components: {}, computedAt: '2025-05-20T10:00:00Z' } });
    live.seedRisk([{ patientId: 'p1', score: 1, band: 'Low', components: {}, computedAt: '2025-05-20T09:00:00Z' }]);
    expect(live.risk()['p1']?.score).toBe(7);
  });
});
