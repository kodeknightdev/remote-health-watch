import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideEchartsCore } from 'ngx-echarts';
import { of } from 'rxjs';
import { PatientsApi, RiskApi } from '../../core/api.service';
import { PatientSummary } from '../../core/models';
import { PatientListComponent, PatientRow, sortByRisk } from './patient-list.component';

const patient = (id: string, name: string): PatientSummary => ({
  id,
  name,
  mrn: 'MRN-' + id,
  age: 70,
  sex: 'F',
  conditions: [],
  status: 'MonitoringActive',
  enrolledAt: '2025-05-01T00:00:00Z',
});

describe('sortByRisk', () => {
  it('orders by score, then band, then name', () => {
    const rows: PatientRow[] = [
      { ...patient('a', 'Adams'), score: 3 },
      { ...patient('b', 'Baker'), score: 9 },
      { ...patient('c', 'Clark'), score: 3 },
      { ...patient('d', 'Dunn'), score: null },
    ];
    const sorted = sortByRisk(rows, { a: 'Low', b: 'High', c: 'LowMedium' });
    expect(sorted.map((r) => r.id)).toEqual(['b', 'c', 'a', 'd']);
  });
});

describe('PatientListComponent', () => {
  it('lists patients with the highest NEWS2 first', async () => {
    TestBed.configureTestingModule({
      imports: [PatientListComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideNoopAnimations(),
        provideEchartsCore({ echarts: () => import('echarts') }),
        { provide: PatientsApi, useValue: { list: () => of([patient('p1', 'Low Risk'), patient('p2', 'High Risk')]) } },
        {
          provide: RiskApi,
          useValue: {
            scores: () =>
              of([
                { patientId: 'p1', score: 1, band: 'Low', components: {}, computedAt: '' },
                { patientId: 'p2', score: 8, band: 'High', components: {}, computedAt: '' },
              ]),
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(PatientListComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const names = Array.from(fixture.nativeElement.querySelectorAll('.name')).map((e) => (e as HTMLElement).textContent?.trim());
    expect(names).toEqual(['High Risk', 'Low Risk']);
  });
});
