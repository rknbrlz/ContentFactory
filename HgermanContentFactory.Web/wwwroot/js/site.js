/* ============================================================
   Hgerman Content Factory — site.js
   ============================================================ */

// ── Toast Notifications ─────────────────────────────────────

function showToast(message, type = 'info', duration = 3500) {
    let container = document.getElementById('toastContainer');
    if (!container) {
        container = document.createElement('div');
        container.id = 'toastContainer';
        document.body.appendChild(container);
    }

    const toast = document.createElement('div');
    toast.className = `hg-toast hg-toast-${type}`;
    toast.innerHTML = `<i class="bi bi-${iconFor(type)} me-2"></i>${message}`;
    container.appendChild(toast);

    setTimeout(() => {
        toast.style.opacity = '0';
        toast.style.transition = 'opacity .3s';
        setTimeout(() => toast.remove(), 350);
    }, duration);
}

function iconFor(type) {
    return { success: 'check-circle-fill', danger: 'exclamation-triangle-fill',
             info: 'info-circle-fill', warning: 'exclamation-circle-fill' }[type] ?? 'info-circle-fill';
}

// ── CSRF Token helper ───────────────────────────────────────

function getAntiForgeryToken() {
    const el = document.querySelector('input[name="__RequestVerificationToken"]');
    return el ? el.value : '';
}

async function postWithToken(url, body = {}) {
    return fetch(url, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': getAntiForgeryToken()
        },
        body: JSON.stringify(body)
    });
}

// ── Auto-dismiss alerts ─────────────────────────────────────

document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('.alert.alert-success, .alert.alert-info').forEach(el => {
        setTimeout(() => {
            el.style.transition = 'opacity .5s';
            el.style.opacity = '0';
            setTimeout(() => el.remove(), 520);
        }, 4000);
    });
});

// ── SignalR Progress ────────────────────────────────────────

function connectSignalR(channelId) {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/progress')
        .withAutomaticReconnect()
        .build();

    connection.on('VideoStatusUpdated', data => {
        const badge = document.getElementById(`status_${data.videoId}`);
        if (badge) {
            badge.textContent = data.status;
            badge.className   = `badge ${statusBadgeClass(data.status)}`;
        }
        if (data.status === 'Published') {
            showToast(`Video published! ${data.youtubeUrl ? `<a href="${data.youtubeUrl}" target="_blank" class="text-white">Watch</a>` : ''}`, 'success', 5000);
        }
    });

    connection.start()
        .then(() => connection.invoke('JoinChannel', `channel-${channelId}`))
        .catch(err => console.warn('SignalR:', err));

    return connection;
}

function statusBadgeClass(status) {
    const map = {
        'Published':   'bg-success',
        'Scheduled':   'bg-info text-dark',
        'Rendered':    'bg-primary',
        'ScriptReady': 'bg-info text-dark',
        'Failed':      'bg-danger',
        'Cancelled':   'bg-secondary',
        'Pending':     'bg-warning text-dark'
    };
    return map[status] ?? 'bg-secondary';
}

// ── Confirm delete helper ───────────────────────────────────

document.addEventListener('submit', e => {
    const form = e.target;
    if (form.dataset.confirm) {
        if (!confirm(form.dataset.confirm)) e.preventDefault();
    }
});
