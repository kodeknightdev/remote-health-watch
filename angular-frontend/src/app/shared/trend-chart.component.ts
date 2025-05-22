import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgxEchartsDirective } from 'ngx-echarts';
import type { EChartsCoreOption } from 'echarts/core';
import { Threshold, TrendResponse } from '../core/models';
import { VITAL_LABEL } from '../core/vitals';

@Component({
  selector: 'rhw-trend-chart',
  imports: [NgxEchartsDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div echarts class="chart" [options]="options()" [autoResize]="true"></div>`,
  styles: `.chart { width: 100%; height: 260px; }`,
})
export class TrendChartComponent {
  readonly trend = input<TrendResponse | null>(null);
  readonly thresholds = input<Threshold[]>([]);

  readonly options = computed<EChartsCoreOption>(() => {
    const t = this.trend();
    if (!t) return {};
    const isBp = t.vital === 'BloodPressure';
    const pts = t.points.map((p) => [p.t.endsWith('Z') ? p.t : p.t + 'Z', p.avg] as [string, number]);
    const band = t.points.map((p) => [p.t.endsWith('Z') ? p.t : p.t + 'Z', p.min, p.max]);
    const metric = isBp ? 'Systolic' : t.vital;
    const limit = this.thresholds().find((x) => x.metric === metric);
    const markLines = [
      limit?.lowCritical != null ? { yAxis: limit.lowCritical, name: 'low critical' } : null,
      limit?.highCritical != null ? { yAxis: limit.highCritical, name: 'high critical' } : null,
      limit?.lowWarning != null ? { yAxis: limit.lowWarning, name: 'low warning' } : null,
      limit?.highWarning != null ? { yAxis: limit.highWarning, name: 'high warning' } : null,
    ].filter((x) => x !== null);

    const series: unknown[] = [
      {
        name: isBp ? 'Systolic' : VITAL_LABEL[t.vital],
        type: 'line',
        showSymbol: false,
        data: pts,
        markLine: { silent: true, symbol: 'none', lineStyle: { type: 'dashed', color: '#c62828', opacity: 0.6 }, data: markLines },
      },
      {
        name: 'range',
        type: 'custom',
        silent: true,
        data: band,
        renderItem: (_p: unknown, api: any) => {
          const lo = api.coord([api.value(0), api.value(1)]);
          const hi = api.coord([api.value(0), api.value(2)]);
          return { type: 'line', shape: { x1: lo[0], y1: lo[1], x2: hi[0], y2: hi[1] }, style: { stroke: '#90caf9', lineWidth: 1 } };
        },
        z: -1,
      },
    ];
    if (isBp) {
      series.push({
        name: 'Diastolic',
        type: 'line',
        showSymbol: false,
        data: t.points.filter((p) => p.avg2 != null).map((p) => [p.t.endsWith('Z') ? p.t : p.t + 'Z', p.avg2]),
      });
    }
    return {
      animation: false,
      tooltip: { trigger: 'axis' },
      legend: { show: isBp, top: 0 },
      grid: { left: 48, right: 16, top: 28, bottom: 32 },
      xAxis: { type: 'time' },
      yAxis: { type: 'value', scale: true, name: t.unit },
      series,
    };
  });
}
