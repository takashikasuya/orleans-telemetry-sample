const charts = {};
const palette = ['#1d4ed8', '#dc2626', '#059669', '#7c3aed', '#ea580c', '#0f766e', '#be123c', '#4338ca'];

window.initializeChart = function (chartId) {
    const container = document.getElementById(chartId);
    if (!container || charts[chartId]) return;

    const canvas = document.createElement('canvas');
    canvas.className = 'telemetry-chart-canvas';
    container.appendChild(canvas);

    const tooltip = document.createElement('div');
    tooltip.className = 'telemetry-chart-tooltip';
    container.appendChild(tooltip);

    const chart = {
        container,
        canvas,
        tooltip,
        ctx: canvas.getContext('2d'),
        series: [],
        layout: { points: [] },
        hover: null,
        resizeObserver: null
    };

    canvas.addEventListener('mousemove', evt => onPointerMove(chart, evt));
    canvas.addEventListener('mouseleave', () => clearHover(chart));

    chart.resizeObserver = new ResizeObserver(() => {
        resizeCanvas(chart);
        drawChart(chart);
    });
    chart.resizeObserver.observe(container);

    resizeCanvas(chart);
    charts[chartId] = chart;
    drawChart(chart);
};

window.updateChart = function (chartId, series) {
    const chart = charts[chartId];
    if (!chart) return;

    chart.series = (series || []).map((line, idx) => ({
        pointId: line.pointId,
        label: line.label,
        color: palette[idx % palette.length],
        timestamps: (line.timestamps || []).map(ts => new Date(ts)),
        values: (line.values || []).map(v => {
            const parsed = Number.parseFloat(v);
            return Number.isFinite(parsed) ? parsed : null;
        })
    }));

    chart.hover = null;
    hideTooltip(chart);
    drawChart(chart);
};

function resizeCanvas(chart) {
    const rect = chart.container.getBoundingClientRect();
    const cssWidth = Math.max(320, Math.floor(rect.width));
    const cssHeight = Math.max(280, Math.floor(rect.height));
    const dpr = window.devicePixelRatio || 1;

    chart.canvas.width = Math.floor(cssWidth * dpr);
    chart.canvas.height = Math.floor(cssHeight * dpr);
    chart.canvas.style.width = `${cssWidth}px`;
    chart.canvas.style.height = `${cssHeight}px`;
    chart.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}

function drawChart(chart) {
    const ctx = chart.ctx;
    const width = chart.canvas.clientWidth;
    const height = chart.canvas.clientHeight;

    ctx.clearRect(0, 0, width, height);
    chart.layout.points = [];

    const allPoints = [];
    chart.series.forEach((line, lineIndex) => {
        line.values.forEach((value, index) => {
            if (value !== null) {
                allPoints.push({ value, index, lineIndex, timestamp: line.timestamps[index] });
            }
        });
    });

    if (allPoints.length === 0) {
        ctx.fillStyle = '#6b7280';
        ctx.font = '14px "Segoe UI", sans-serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText('No data available', width / 2, height / 2);
        return;
    }

    const padding = { top: 16, right: 20, bottom: 56, left: 56 };
    const legendHeight = Math.max(0, chart.series.length) * 18;
    const chartWidth = Math.max(1, width - padding.left - padding.right);
    const chartHeight = Math.max(1, height - padding.top - padding.bottom - legendHeight);

    const minValue = Math.min(...allPoints.map(p => p.value));
    const maxValue = Math.max(...allPoints.map(p => p.value));
    const valueRange = maxValue - minValue || 1;

    const maxLength = Math.max(...chart.series.map(s => s.values.length), 1);
    const toX = index => padding.left + (index / Math.max(maxLength - 1, 1)) * chartWidth;
    const toY = value => padding.top + (1 - (value - minValue) / valueRange) * chartHeight;

    // Draw horizontal grid lines
    ctx.strokeStyle = '#d1d5db';
    ctx.lineWidth = 1;
    for (let i = 0; i <= 5; i++) {
        const y = padding.top + (i / 5) * chartHeight;
        ctx.beginPath();
        ctx.moveTo(padding.left, y);
        ctx.lineTo(width - padding.right, y);
        ctx.stroke();
    }

    // Draw data lines
    chart.series.forEach((line, lineIndex) => {
        ctx.strokeStyle = line.color;
        ctx.lineWidth = 2;
        ctx.beginPath();
        let started = false;

        line.values.forEach((value, index) => {
            if (value === null) return;
            const x = toX(index);
            const y = toY(value);
            if (!started) {
                ctx.moveTo(x, y);
                started = true;
            } else {
                ctx.lineTo(x, y);
            }
            chart.layout.points.push({ x, y, value, index, lineIndex, timestamp: line.timestamps[index], color: line.color, label: line.label });
        });
        ctx.stroke();
    });

    // Draw data point circles
    chart.layout.points.forEach((point, idx) => {
        const isHover = chart.hover === idx;
        ctx.fillStyle = point.color;
        ctx.beginPath();
        ctx.arc(point.x, point.y, isHover ? 4 : 3, 0, Math.PI * 2);
        ctx.fill();
    });

    // Draw Y-axis labels
    ctx.fillStyle = '#4b5563';
    ctx.font = '11px "Segoe UI", sans-serif';
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    for (let i = 0; i <= 5; i++) {
        const value = maxValue - (i / 5) * valueRange;
        const y = padding.top + (i / 5) * chartHeight;
        ctx.fillText(value.toFixed(2), padding.left - 8, y);
    }

    // Draw X-axis time labels
    drawTimeAxis(chart, ctx, padding, chartWidth, chartHeight, maxLength);

    drawLegend(chart, padding.left, padding.top + chartHeight + 36);
}

function drawTimeAxis(chart, ctx, padding, chartWidth, chartHeight, maxLength) {
    // Collect all unique timestamps across all series
    const allTimestamps = [];
    chart.series.forEach(line => {
        line.timestamps.forEach((ts, idx) => {
            if (ts instanceof Date && !Number.isNaN(ts.getTime())) {
                allTimestamps.push({ idx, ts });
            }
        });
    });

    if (allTimestamps.length === 0) return;

    // Determine a good number of labels to show (max 6)
    const labelCount = Math.min(6, maxLength);
    if (labelCount < 2) return;

    // Find min/max timestamps for even distribution
    const sortedTs = allTimestamps.map(a => a.ts.getTime()).sort((a, b) => a - b);
    const minTs = sortedTs[0];
    const maxTs = sortedTs[sortedTs.length - 1];
    const tsRange = maxTs - minTs;

    ctx.fillStyle = '#6b7280';
    ctx.font = '10px "Segoe UI", sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';

    const baseY = padding.top + chartHeight + 4;

    for (let i = 0; i < labelCount; i++) {
        const ratio = i / (labelCount - 1);
        const x = padding.left + ratio * chartWidth;
        const targetTs = new Date(minTs + ratio * tsRange);
        const label = formatTimeLabel(targetTs, tsRange);

        // Draw tick mark
        ctx.strokeStyle = '#d1d5db';
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(x, padding.top + chartHeight);
        ctx.lineTo(x, padding.top + chartHeight + 3);
        ctx.stroke();

        ctx.fillText(label, x, baseY);
    }
}

function formatTimeLabel(date, rangeMs) {
    if (!(date instanceof Date) || Number.isNaN(date.getTime())) return '';

    const pad = n => String(n).padStart(2, '0');
    const hours = pad(date.getHours());
    const minutes = pad(date.getMinutes());
    const seconds = pad(date.getSeconds());

    // If range is less than 1 hour, show HH:mm:ss
    if (rangeMs < 3600000) {
        return `${hours}:${minutes}:${seconds}`;
    }
    // If range is less than 24 hours, show HH:mm
    if (rangeMs < 86400000) {
        return `${hours}:${minutes}`;
    }
    // Otherwise show MM/DD HH:mm
    const month = pad(date.getMonth() + 1);
    const day = pad(date.getDate());
    return `${month}/${day} ${hours}:${minutes}`;
}

function drawLegend(chart, x, y) {
    const ctx = chart.ctx;
    ctx.font = '12px "Segoe UI", sans-serif';
    ctx.textAlign = 'left';
    ctx.textBaseline = 'middle';

    chart.series.forEach((line, idx) => {
        const rowY = y + idx * 18;
        ctx.fillStyle = line.color;
        ctx.fillRect(x, rowY - 5, 10, 10);
        ctx.fillStyle = '#374151';
        ctx.fillText(line.label || line.pointId, x + 16, rowY);
    });
}

function onPointerMove(chart, event) {
    if (!chart.layout.points.length) return;
    const rect = chart.canvas.getBoundingClientRect();
    const pointerX = event.clientX - rect.left;
    const pointerY = event.clientY - rect.top;

    let nearest = null;
    let bestDistanceSquared = Number.POSITIVE_INFINITY;
    chart.layout.points.forEach((point, index) => {
        const dx = point.x - pointerX;
        const dy = point.y - pointerY;
        const distanceSquared = dx * dx + dy * dy;
        if (distanceSquared < bestDistanceSquared) {
            bestDistanceSquared = distanceSquared;
            nearest = { point, index };
        }
    });

    if (!nearest || bestDistanceSquared > 24 * 24) {
        clearHover(chart);
        return;
    }

    chart.hover = nearest.index;
    drawChart(chart);
    showTooltip(chart, nearest.point, event.clientX, event.clientY);
}

function clearHover(chart) {
    if (chart.hover === null) {
        hideTooltip(chart);
        return;
    }

    chart.hover = null;
    hideTooltip(chart);
    drawChart(chart);
}

function showTooltip(chart, point, clientX, clientY) {
    const formattedTime = formatDateTime(point.timestamp);
    // Build tooltip content using DOM API to avoid XSS from point.label
    chart.tooltip.textContent = '';
    const labelEl = document.createElement('strong');
    labelEl.textContent = point.label;
    chart.tooltip.appendChild(labelEl);
    chart.tooltip.appendChild(document.createElement('br'));
    chart.tooltip.appendChild(document.createTextNode(formattedTime));
    chart.tooltip.appendChild(document.createElement('br'));
    chart.tooltip.appendChild(document.createTextNode('Value: ' + point.value.toFixed(3)));
    chart.tooltip.style.display = 'block';

    const containerRect = chart.container.getBoundingClientRect();
    const tooltipRect = chart.tooltip.getBoundingClientRect();
    const desiredX = clientX - containerRect.left + 12;
    const desiredY = clientY - containerRect.top - tooltipRect.height - 12;

    chart.tooltip.style.left = `${Math.min(Math.max(8, desiredX), containerRect.width - tooltipRect.width - 8)}px`;
    chart.tooltip.style.top = `${Math.max(8, desiredY)}px`;
}

function hideTooltip(chart) {
    chart.tooltip.style.display = 'none';
}

// Formats a Date for the chart tooltip in local time (browser timezone).
// Server-side timestamps in Admin.razor use FormatTimestamp() which displays UTC.
function formatDateTime(value) {
    if (!(value instanceof Date) || Number.isNaN(value.getTime())) {
        return '-';
    }

    const pad = n => String(n).padStart(2, '0');
    const y = value.getFullYear();
    const m = pad(value.getMonth() + 1);
    const d = pad(value.getDate());
    const hh = pad(value.getHours());
    const mm = pad(value.getMinutes());
    const ss = pad(value.getSeconds());
    return `${y}-${m}-${d} ${hh}:${mm}:${ss}`;
}
