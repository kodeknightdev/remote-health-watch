import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { NgxEchartsDirective } from 'ngx-echarts';
import type { EChartsCoreOption } from 'echarts/core';

@Component({
  selector: 'rhw-sparkline',
  imports: [NgxEchartsDirective],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div echarts class="spark" [options]="base" [merge]="merge()" [autoResize]="true"></div>`,
  styles: `
    .spark {
      width: 100%;
      height: 28px;
    }
  `,
})
export class SparklineComponent {
  readonly values = input<number[]>([]);
  readonly color = input('#1565c0');

  readonly base: EChartsCoreOption = {
    animation: false,
    grid: { left: 0, right: 0, top: 2, bottom: 2 },
    xAxis: { type: 'category', show: false },
    yAxis: { type: 'value', show: false, scale: true },
    series: [{ type: 'line', data: [], showSymbol: false, smooth: true, lineStyle: { width: 1.5 } }],
  };

  // only the data changes per push; merging avoids rebuilding the chart on every reading
  readonly merge = computed<EChartsCoreOption>(() => ({
    series: [{ data: this.values(), lineStyle: { color: this.color() }, itemStyle: { color: this.color() } }],
  }));
}
