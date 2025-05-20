import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { News2Band } from '../core/models';
import { bandLabel } from '../core/vitals';

@Component({
  selector: 'rhw-news2-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (score() !== null) {
      <span class="badge" [class]="'band-' + band()" [attr.title]="'NEWS2 ' + score() + ' (' + label() + ')'">
        <strong>{{ score() }}</strong> <small>{{ label() }}</small>
      </span>
    } @else {
      <span class="badge band-none">–</span>
    }
  `,
  styles: `
    .badge { display: inline-flex; gap: 4px; align-items: baseline; padding: 2px 8px; border-radius: 12px; font-size: 13px; white-space: nowrap; }
    .band-Low { background: #e6f4ea; color: #1e6b34; }
    .band-LowMedium { background: #fff4d6; color: #7a5a00; }
    .band-Medium { background: #ffe1c2; color: #8a3d00; }
    .band-High { background: #c62828; color: #fff; }
    .band-none { background: #eceff1; color: #607d8b; }
  `,
})
export class News2BadgeComponent {
  readonly score = input<number | null>(null);
  readonly band = input<News2Band>('Low');
  readonly label = computed(() => bandLabel(this.band()));
}
