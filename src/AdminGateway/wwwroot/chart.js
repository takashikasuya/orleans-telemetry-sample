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

    const padding = { top: 16, right: 20, bottom: 40, left: 56 };
    const legendHeight = Math.max(0, chart.series.length) * 18;
    const chartWidth = Math.max(1, width - padding.left - padding.right);
    const chartHeight = Math.max(1, height - padding.top - padding.bottom - legendHeight);

    const minValue = Math.min(...allPoints.map(p => p.value));
    const maxValue = Math.max(...allPoints.map(p => p.value));
    const valueRange = maxValue - minValue || 1;

    const maxLength = Math.max(...chart.series.map(s => s.values.length), 1);
    const toX = index => padding.left + (index / Math.max(maxLength - 1, 1)) * chartWidth;
    const toY = value => padding.top + (1 - (value - minValue) / valueRange) * chartHeight;

    ctx.strokeStyle = '#d1d5db';
    ctx.lineWidth = 1;
    for (let i = 0; i <= 5; i++) {
        const y = padding.top + (i / 5) * chartHeight;
        ctx.beginPath();
        ctx.moveTo(padding.left, y);
        ctx.lineTo(width - padding.right, y);
        ctx.stroke();
    }

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

    chart.layout.points.forEach((point, idx) => {
        const isHover = chart.hover === idx;
        ctx.fillStyle = point.color;
        ctx.beginPath();
        ctx.arc(point.x, point.y, isHover ? 4 : 3, 0, Math.PI * 2);
        ctx.fill();
    });

    ctx.fillStyle = '#4b5563';
    ctx.font = '11px "Segoe UI", sans-serif';
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    for (let i = 0; i <= 5; i++) {
        const value = maxValue - (i / 5) * valueRange;
        const y = padding.top + (i / 5) * chartHeight;
        ctx.fillText(value.toFixed(2), padding.left - 8, y);
    }

    drawLegend(chart, padding.left, padding.top + chartHeight + 20);
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
    let bestDistance = Number.POSITIVE_INFINITY;
    chart.layout.points.forEach((point, index) => {
        const dx = point.x - pointerX;
        const dy = point.y - pointerY;
        const distance = Math.sqrt(dx * dx + dy * dy);
        if (distance < bestDistance) {
            bestDistance = distance;
            nearest = { point, index };
        }
    });

    if (!nearest || bestDistance > 24) {
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
    chart.tooltip.textContent = `${point.label}: ${formattedTime} | ${point.value.toFixed(3)}`;
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

function formatDateTime(value) {
    if (!(value instanceof Date) || Number.isNaN(value.getTime())) {
        return '-';
    }

    return new Intl.DateTimeFormat(undefined, {
        year: 'numeric', month: '2-digit', day: '2-digit',
        hour: '2-digit', minute: '2-digit', second: '2-digit'
    }).format(value);
}
