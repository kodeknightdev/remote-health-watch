import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DecimalPipe, PercentPipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { NgxEchartsDirective } from 'ngx-echarts';
import type { EChartsCoreOption } from 'echarts/core';
import { InsightsApi } from '../../core/api.service';
import { Kpis } from '../../core/models';

@Component({
  selector: 'rhw-programme',
  imports: [MatCardModule, NgxEchartsDirective, DecimalPipe, PercentPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Programme</h2>
    @if (kpis(); as k) {
      <div class="cards">
        <mat-card><mat-card-content><div class="n">{{ k.enrolledPatients }}</div>enrolled ({{ k.activePatients }} active)</mat-card-content></mat-card>
        <mat-card><mat-card-content>
          <div class="n">{{ k.averageMinutesToAcknowledge === null ? '–' : (k.averageMinutesToAcknowledge | number: '1.0-1') }}</div>avg minutes to acknowledge
        </mat-card-content></mat-card>
        <mat-card><mat-card-content><div class="n">{{ k.escalationRate | percent: '1.0-1' }}</div>escalation rate</mat-card-content></mat-card>
        <mat-card><mat-card-content>
          <div class="n">{{ k.wearTimeCompliance === null ? '–' : (k.wearTimeCompliance | percent: '1.0-0') }}</div>device wear-time compliance
        </mat-card-content></mat-card>
      </div>
      <mat-card class="chart-card"><mat-card-content><div echarts class="chart" [options]="chart()"></div></mat-card-content></mat-card>
    } @else {
      <p class="muted">Loading programme metrics…</p>
    }
  `,
  styles: `
    .cards { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; }
    .n { font-size: 28px; font-weight: 500; }
    .chart-card { margin-top: 12px; }
    .chart { height: 280px; }
    .muted { color: #78909c; }
  `,
})
export class ProgrammeComponent implements OnInit {
  private readonly api = inject(InsightsApi);
  readonly kpis = signal<Kpis | null>(null);
  readonly chart = computed<EChartsCoreOption>(() => {
    const k = this.kpis();
    if (!k) return {};
    return {
      title: { text: 'Alerts per day', textStyle: { fontSize: 13 } },
      tooltip: { trigger: 'axis' },
      grid: { left: 40, right: 16, top: 40, bottom: 28 },
      xAxis: { type: 'category', data: k.alertsPerDay.map((d) => d.day) },
      yAxis: { type: 'value', minInterval: 1 },
      series: [{ type: 'bar', data: k.alertsPerDay.map((d) => d.count), itemStyle: { color: '#1565c0' } }],
    };
  });

  ngOnInit(): void {
    this.api.kpis().subscribe((k) => this.kpis.set(k));
  }
}
