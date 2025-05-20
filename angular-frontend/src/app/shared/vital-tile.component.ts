import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { LatestValue, VitalType } from '../core/models';
import { VITAL_LABEL, VITAL_UNIT, formatVital } from '../core/vitals';
import { SparklineComponent } from './sparkline.component';

@Component({
  selector: 'rhw-vital-tile',
  imports: [SparklineComponent, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="tile" [class.stale]="stale()" [class.compact]="compact()">
      <div class="label">{{ label() }}</div>
      <div class="value">
        @if (latest(); as v) {
          {{ display() }} <span class="unit">{{ unit() }}</span>
        } @else {
          <span class="empty">–</span>
        }
      </div>
      @if (!compact()) {
        <rhw-sparkline [values]="history()" />
        @if (latest(); as v) {
          <div class="at">{{ v.measuredAt | date: 'HH:mm:ss' }}</div>
        }
      }
    </div>
  `,
  styles: `
    .tile { border: 1px solid #e0e0e0; border-radius: 8px; padding: 6px 8px; min-width: 88px; background: #fff; }
    .tile.compact { border: none; padding: 0 4px; min-width: 56px; }
    .tile.stale { opacity: 0.55; }
    .label { font-size: 11px; color: #607d8b; text-transform: uppercase; letter-spacing: 0.04em; }
    .value { font-size: 20px; font-weight: 500; font-variant-numeric: tabular-nums; }
    .compact .value { font-size: 15px; }
    .unit { font-size: 11px; color: #78909c; font-weight: 400; }
    .at { font-size: 10px; color: #90a4ae; text-align: right; }
    .empty { color: #b0bec5; }
  `,
})
export class VitalTileComponent {
  readonly vital = input.required<VitalType>();
  readonly latest = input<LatestValue | undefined>();
  readonly history = input<number[]>([]);
  readonly compact = input(false);

  readonly label = computed(() => VITAL_LABEL[this.vital()]);
  readonly unit = computed(() => VITAL_UNIT[this.vital()]);
  readonly display = computed(() => {
    const v = this.latest();
    return v ? formatVital(this.vital(), v.value, v.value2) : '';
  });
  // older than 15 minutes counts as stale on the dashboard
  readonly stale = computed(() => {
    const v = this.latest();
    return !!v && Date.now() - new Date(v.measuredAt).getTime() > 15 * 60_000;
  });
}
