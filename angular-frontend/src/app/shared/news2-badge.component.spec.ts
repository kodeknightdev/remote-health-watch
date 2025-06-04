import { TestBed } from '@angular/core/testing';
import { News2BadgeComponent } from './news2-badge.component';

describe('News2BadgeComponent', () => {
  it('renders score and band class', () => {
    const fixture = TestBed.createComponent(News2BadgeComponent);
    fixture.componentRef.setInput('score', 7);
    fixture.componentRef.setInput('band', 'High');
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement.querySelector('.badge');
    expect(el.classList).toContain('band-High');
    expect(el.textContent).toContain('7');
  });

  it('labels a single red parameter as low-medium', () => {
    const fixture = TestBed.createComponent(News2BadgeComponent);
    fixture.componentRef.setInput('score', 3);
    fixture.componentRef.setInput('band', 'LowMedium');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Low–medium');
  });

  it('shows a dash before the first score', () => {
    const fixture = TestBed.createComponent(News2BadgeComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.band-none')).not.toBeNull();
  });
});
