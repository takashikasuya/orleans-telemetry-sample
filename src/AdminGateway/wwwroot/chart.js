const charts = {};

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
        data: { timestamps: [], values: [] },
        layout: { points: [], bounds: null },
        hoverIndex: null,
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

window.updateChart = function (chartId, timestamps, values) {
    console.log(`[chart.js] updateChart called: chartId=${chartId}, timestamps.length=${timestamps?.length}, values.length=${values?.length}`);
    const chart = charts[chartId];
    if (!chart) {
        console.warn(`[chart.js] Chart ${chartId} not found in charts registry`);
        return;
    }

    console.log(`[chart.js] Updating chart data with ${timestamps.length} timestamps and ${values.length} values`);
    chart.data.timestamps = timestamps.map(ts => new Date(ts));
    chart.data.values = values.map(v => {
        const parsed = Number.parseFloat(v);
        return Number.isFinite(parsed) ? parsed : null;
    });
    console.log(`[chart.js] Processed values (nulls replaced): ${chart.data.values.slice(0, 5).join(', ')}...`);
    chart.hoverIndex = null;
    hideTooltip(chart);
    drawChart(chart);
    console.log(`[chart.js] Chart rendering completed for ${chartId}`);
};

function resizeCanvas(chart) {
    const rect = chart.container.getBoundingClientRect();
    const cssWidth = Math.max(320, Math.floor(rect.width));
    const cssHeight = Math.max(260, Math.floor(rect.height));
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
    const { timestamps, values } = chart.data;
    const points = [];

    ctx.clearRect(0, 0, width, height);
    chart.layout.points = points;
    chart.layout.bounds = null;

    const valid = values
        .map((value, index) => ({ value, index }))
        .filter(item => item.value !== null);

    if (valid.length === 0) {
        ctx.fillStyle = '#6b7280';
        ctx.font = '14px "Segoe UI", sans-serif';
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText('No data available', width / 2, height / 2);
        return;
    }

    const padding = { top: 16, right: 20, bottom: 34, left: 56 };
    const chartWidth = Math.max(1, width - padding.left - padding.right);
    const chartHeight = Math.max(1, height - padding.top - padding.bottom);
    const minValue = Math.min(...valid.map(item => item.value));
    const maxValue = Math.max(...valid.map(item => item.value));
    const valueRange = maxValue - minValue || 1;
    const yTicks = 5;
    const xTicks = Math.min(5, valid.length);

    const toX = index => padding.left + (index / Math.max(values.length - 1, 1)) * chartWidth;
    const toY = value => padding.top + (1 - (value - minValue) / valueRange) * chartHeight;

    chart.layout.bounds = {
        left: padding.left,
        top: padding.top,
        right: width - padding.right,
        bottom: height - padding.bottom
    };

    ctx.strokeStyle = '#d1d5db';
    ctx.lineWidth = 1;
    for (let i = 0; i <= yTicks; i++) {
        const y = padding.top + (i / yTicks) * chartHeight;
        ctx.beginPath();
        ctx.moveTo(padding.left, y);
        ctx.lineTo(width - padding.right, y);
        ctx.stroke();
    }
    for (let i = 0; i <= xTicks; i++) {
        const x = padding.left + (i / Math.max(xTicks, 1)) * chartWidth;
        ctx.beginPath();
        ctx.moveTo(x, padding.top);
        ctx.lineTo(x, height - padding.bottom);
        ctx.stroke();
    }

    ctx.strokeStyle = '#9ca3af';
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.moveTo(padding.left, padding.top);
    ctx.lineTo(padding.left, height - padding.bottom);
    ctx.lineTo(width - padding.right, height - padding.bottom);
    ctx.stroke();

    ctx.strokeStyle = '#1d4ed8';
    ctx.lineWidth = 2;
    ctx.beginPath();
    let started = false;
    values.forEach((value, index) => {
        if (value === null) return;
        const x = toX(index);
        const y = toY(value);
        if (!started) {
            ctx.moveTo(x, y);
            started = true;
        } else {
            ctx.lineTo(x, y);
        }
        points.push({ x, y, value, index, timestamp: timestamps[index] });
    });
    ctx.stroke();

    points.forEach((point, idx) => {
        const isHover = chart.hoverIndex === idx;
        ctx.fillStyle = isHover ? '#0f172a' : '#1d4ed8';
        ctx.beginPath();
        ctx.arc(point.x, point.y, isHover ? 4 : 3, 0, Math.PI * 2);
        ctx.fill();
    });

    ctx.fillStyle = '#4b5563';
    ctx.font = '11px "Segoe UI", sans-serif';
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    for (let i = 0; i <= yTicks; i++) {
        const value = maxValue - (i / yTicks) * valueRange;
        const y = padding.top + (i / yTicks) * chartHeight;
        ctx.fillText(value.toFixed(2), padding.left - 8, y);
    }

    const labels = buildXLabels(timestamps, values.length);
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    labels.forEach(label => {
        ctx.fillText(label.text, toX(label.index), height - padding.bottom + 8);
    });
}

function onPointerMove(chart, event) {
    if (!chart.layout.points.length) {
        return;
    }

    const rect = chart.canvas.getBoundingClientRect();
    const pointerX = event.clientX - rect.left;
    const pointerY = event.clientY - rect.top;
    const nearest = getNearestPoint(chart.layout.points, pointerX, pointerY);
    if (!nearest) {
        clearHover(chart);
        return;
    }

    chart.hoverIndex = nearest.index;
    drawChart(chart);
    showTooltip(chart, nearest.point, event.clientX, event.clientY);
}

function clearHover(chart) {
    if (chart.hoverIndex === null) {
        hideTooltip(chart);
        return;
    }

    chart.hoverIndex = null;
    hideTooltip(chart);
    drawChart(chart);
}

function getNearestPoint(points, x, y) {
    let nearest = null;
    let bestDistance = Number.POSITIVE_INFINITY;
    points.forEach((point, index) => {
        const dx = point.x - x;
        const dy = point.y - y;
        const distance = Math.sqrt(dx * dx + dy * dy);
        if (distance < bestDistance) {
            bestDistance = distance;
            nearest = { point, index };
        }
    });

    return bestDistance <= 24 ? nearest : null;
}

function showTooltip(chart, point, clientX, clientY) {
    const formattedTime = formatDateTime(point.timestamp);
    chart.tooltip.textContent = `${formattedTime} | ${point.value.toFixed(3)}`;
    chart.tooltip.style.display = 'block';

    const containerRect = chart.container.getBoundingClientRect();
    const tooltipRect = chart.tooltip.getBoundingClientRect();
    const desiredX = clientX - containerRect.left + 12;
    const desiredY = clientY - containerRect.top - tooltipRect.height - 12;
    const clampedX = Math.min(
        Math.max(8, desiredX),
        containerRect.width - tooltipRect.width - 8
    );
    const clampedY = Math.max(8, desiredY);

    chart.tooltip.style.left = `${clampedX}px`;
    chart.tooltip.style.top = `${clampedY}px`;
}

function hideTooltip(chart) {
    chart.tooltip.style.display = 'none';
}

function buildXLabels(timestamps, length) {
    if (!timestamps.length) {
        return [];
    }

    const first = 0;
    const mid = Math.floor((length - 1) / 2);
    const last = Math.max(0, length - 1);
    const unique = Array.from(new Set([first, mid, last]));
    return unique.map(index => ({
        index,
        text: formatTime(timestamps[index])
    }));
}

function formatTime(value) {
    if (!(value instanceof Date) || Number.isNaN(value.getTime())) {
        return '-';
    }

    return new Intl.DateTimeFormat(undefined, {
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit'
    }).format(value);
}

function formatDateTime(value) {
    if (!(value instanceof Date) || Number.isNaN(value.getTime())) {
        return '-';
    }

    return new Intl.DateTimeFormat(undefined, {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit'
    }).format(value);
}
