import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { AlertsApi } from '../../core/api.service';
import { AlertSummary } from '../../core/models';
import { AlertInboxComponent } from './alert-inbox.component';

const alert: AlertSummary = {
  id: 'a1', patientId: 'p1', type: 'ThresholdBreach', severity: 'Critical', message: 'SpO2 below critical limit (88 vs 91)',
  status: 'EscalatedDoctor', escalationLevel: 1, assignedRole: 'Doctor', raisedAt: '2025-05-20T10:00:00Z',
  lastOccurredAt: '2025-05-20T10:02:00Z', occurrenceCount: 3, nextEscalationAt: '2025-05-20T10:07:00Z',
};

describe('AlertInboxComponent', () => {
  it('shows the escalation step and acknowledges', () => {
    const api = jasmine.createSpyObj<AlertsApi>('AlertsApi', ['inbox', 'acknowledge']);
    api.inbox.and.returnValue(of([alert]));
    api.acknowledge.and.returnValue(of({ alert: { ...alert, status: 'Acknowledged' }, timeline: [], notes: [] }));
    TestBed.configureTestingModule({
      imports: [AlertInboxComponent],
      providers: [provideHttpClient(), provideRouter([]), provideNoopAnimations(), { provide: AlertsApi, useValue: api }],
    });
    const fixture = TestBed.createComponent(AlertInboxComponent);
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.step.current')?.textContent).toContain('Doctor');
    expect(el.textContent).toContain('repeated 3×');

    (el.querySelector('.alert button') as HTMLButtonElement).click();
    expect(api.acknowledge).toHaveBeenCalledWith('a1');
  });
});
