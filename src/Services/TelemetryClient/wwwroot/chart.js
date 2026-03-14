// Simple chart implementation using HTML Canvas
// Can be replaced with ECharts or Plotly for production

const charts = {};

window.initializeChart = function (chartId) {
    const container = document.getElementById(chartId);
    if (!container) return;

    const canvas = document.createElement('canvas');
    canvas.width = container.clientWidth;
    canvas.height = 400;
    container.appendChild(canvas);

    charts[chartId] = {
        canvas: canvas,
        ctx: canvas.getContext('2d'),
        data: { timestamps: [], values: [] }
    };
};

window.updateChart = function (chartId, timestamps, values) {
    const chart = charts[chartId];
    if (!chart) return;

    chart.data.timestamps = timestamps;
    chart.data.values = values.map(v => parseFloat(v) || 0);

    drawChart(chart);
};

function drawChart(chart) {
    const ctx = chart.ctx;
    const canvas = chart.canvas;
    const { timestamps, values } = chart.data;

    // Clear canvas
    ctx.clearRect(0, 0, canvas.width, canvas.height);

    if (values.length === 0) {
        ctx.fillStyle = '#666';
        ctx.font = '16px Arial';
        ctx.textAlign = 'center';
        ctx.fillText('No data available', canvas.width / 2, canvas.height / 2);
        return;
    }

    // Calculate scales
    const padding = 50;
    const chartWidth = canvas.width - 2 * padding;
    const chartHeight = canvas.height - 2 * padding;

    const minValue = Math.min(...values);
    const maxValue = Math.max(...values);
    const valueRange = maxValue - minValue || 1;

    // Draw axes
    ctx.strokeStyle = '#333';
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(padding, padding);
    ctx.lineTo(padding, canvas.height - padding);
    ctx.lineTo(canvas.width - padding, canvas.height - padding);
    ctx.stroke();

    // Draw line chart
    ctx.strokeStyle = '#1976d2';
    ctx.lineWidth = 2;
    ctx.beginPath();

    values.forEach((value, index) => {
        const x = padding + (index / (values.length - 1 || 1)) * chartWidth;
        const y = canvas.height - padding - ((value - minValue) / valueRange) * chartHeight;

        if (index === 0) {
            ctx.moveTo(x, y);
        } else {
            ctx.lineTo(x, y);
        }
    });

    ctx.stroke();

    // Draw points
    ctx.fillStyle = '#1976d2';
    values.forEach((value, index) => {
        const x = padding + (index / (values.length - 1 || 1)) * chartWidth;
        const y = canvas.height - padding - ((value - minValue) / valueRange) * chartHeight;
        
        ctx.beginPath();
        ctx.arc(x, y, 4, 0, 2 * Math.PI);
        ctx.fill();
    });

    // Draw labels
    ctx.fillStyle = '#666';
    ctx.font = '12px Arial';
    ctx.textAlign = 'center';

    // X-axis labels (timestamps) - show first, middle, and last
    if (timestamps.length > 0) {
        ctx.fillText(timestamps[0], padding, canvas.height - padding + 20);
        if (timestamps.length > 2) {
            const midIndex = Math.floor(timestamps.length / 2);
            ctx.fillText(timestamps[midIndex], canvas.width / 2, canvas.height - padding + 20);
        }
        if (timestamps.length > 1) {
            ctx.fillText(timestamps[timestamps.length - 1], canvas.width - padding, canvas.height - padding + 20);
        }
    }

    // Y-axis labels
    ctx.textAlign = 'right';
    ctx.fillText(maxValue.toFixed(2), padding - 10, padding + 5);
    ctx.fillText(minValue.toFixed(2), padding - 10, canvas.height - padding + 5);
}
