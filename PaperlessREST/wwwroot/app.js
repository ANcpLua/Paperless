(() => {
    // ─── GLOBAL STATE ──────────────────────────────────────────────
    const documents = new Map();
    let eventSource = null;
    let searchQuery = '';

    // ─── UTILITIES ────────────────────────────────────────────────
    const $ = (id) => document.getElementById(id);

    const formatDate = (iso) =>
        iso ? new Date(iso).toLocaleString(undefined, {dateStyle: 'medium', timeStyle: 'short'}) : 'Unknown';

    const notify = (msg, type = 'info') => {
        const template = $('notificationTemplate');
        const notification = template.content.cloneNode(true);
        const container = notification.querySelector('.alert');
        container.classList.add(`alert-${type}`);
        notification.querySelector('[data-field="message"]').textContent = msg;
        $('notificationContainer').appendChild(notification);

        // Auto-remove after 5 seconds
        setTimeout(() => {
            const alerts = $('notificationContainer').querySelectorAll('.alert');
            if (alerts.length > 0) alerts[0].remove();
        }, 5000);
    };

    // ─── DARK MODE ────────────────────────────────────────────────
    function toggleTheme() {
        const html = document.documentElement;
        const currentTheme = html.getAttribute('data-bs-theme');
        const newTheme = currentTheme === 'dark' ? 'light' : 'dark';

        html.setAttribute('data-bs-theme', newTheme);
        $('themeIcon').className = newTheme === 'dark' ? 'bi bi-sun' : 'bi bi-moon';

        // Save preference
        localStorage.setItem('theme', newTheme);
    }

    window.toggleTheme = toggleTheme;

    // ─── SERVER‑SENT EVENTS ───────────────────────────────────────
    function initSSE() {
        eventSource = new EventSource('/api/v1/ocr-results');

        eventSource.onopen = () => $('sseStatus').style.display = 'none';
        eventSource.onerror = () => {
            $('sseStatus').style.display = 'block';
            setTimeout(initSSE, 5000);
        };

        const refresh = async (evt, label) => {
            console.log(label, JSON.parse(evt.data));
            await loadDocuments();
            notify(`${label} for document`, label.includes('completed') ? 'success' : 'danger');
        };

        eventSource.addEventListener('ocr-completed', e => refresh(e, 'OCR completed'));
        eventSource.addEventListener('ocr-failed', e => refresh(e, 'OCR failed'));
    }

    // ─── FILE UPLOAD ──────────────────────────────────────────────
    function setupUploadZone() {
        const zone = $('uploadZone'), input = $('fileInput');

        zone.addEventListener('dragover', e => {
            e.preventDefault();
            zone.classList.add('border-info', 'bg-info', 'bg-opacity-10');
        });
        zone.addEventListener('dragleave', () => {
            zone.classList.remove('border-info', 'bg-info', 'bg-opacity-10');
        });
        zone.addEventListener('drop', e => {
            e.preventDefault();
            zone.classList.remove('border-info', 'bg-info', 'bg-opacity-10');
            handleFiles(e.dataTransfer.files);
        });
        input.addEventListener('change', e => handleFiles(e.target.files));
    }

    async function handleFiles(files) {
        for (const f of files) {
            if (f.type === 'application/pdf') await upload(f);
            else notify(`${f.name} is not a PDF`, 'warning');
        }
    }

    async function upload(file) {
        const body = new FormData();
        body.append('file', file);
        try {
            const res = await fetch('/api/v1/documents', {method: 'POST', body});
            if (!res.ok) {
                const p = await res.json().catch(() => ({}));
                return notify(`Failed: ${p.detail || res.statusText}`, 'danger');
            }
            const doc = await res.json();
            documents.set(doc.id, doc);
            render();
            notify(`Uploaded ${file.name}`, 'success');
        } catch (err) {
            console.error(err);
            notify(`Error uploading ${file.name}`, 'danger');
        }
    }

    // ─── DOCUMENT CRUD ────────────────────────────────────────────
    async function loadDocuments() {
        try {
            const res = await fetch('/api/v1/documents');
            if (!res.ok) return;
            documents.clear();
            (await res.json()).forEach(d => documents.set(d.id, d));
            render();
        } catch (err) {
            console.error(err);
            notify('Failed to load documents', 'danger');
        }
    }

    async function deleteDocument(id) {
        if (!confirm('Delete this document?')) return;
        try {
            const res = await fetch(`/api/v1/documents/${id}`, {method: 'DELETE'});
            if (res.ok) {
                documents.delete(id);
                render();
                notify('Deleted', 'success');
            } else notify('Delete failed', 'danger');
        } catch (err) {
            console.error(err);
            notify('Error deleting', 'danger');
        }
    }

    // ─── SEARCH ──────────────────────
    async function search() {
        const q = $('searchInput').value.trim();
        if (!q) {
            searchQuery = '';
            return loadDocuments();
        }

        const limit = 50;
        const url = `/api/v1/documents/search?query=${encodeURIComponent(q)}&Limit=${limit}`;

        try {
            const res = await fetch(url);
            if (!res.ok) return notify('Server search failed', 'danger');
            const hits = await res.json();
            documents.clear();
            hits.forEach(d => documents.set(d.id || d.Id, {...d, status: d.status || d.Status || 'Completed'}));
            searchQuery = q;
            render();
            notify(`Found ${hits.length} result(s)`, 'info');
        } catch (err) {
            console.error(err);
            notify('Search failed', 'danger');
        }
    }

    // ─── RENDERING ────────────────────────────────────────────────
    function createCard(doc) {
        const template = $('documentCardTemplate');
        const card = template.content.cloneNode(true);

        // Set filename
        card.querySelector('[data-field="fileName"]').textContent = doc.fileName;

        // Set dates
        card.querySelector('[data-field="uploadDate"]').textContent = `Uploaded: ${formatDate(doc.createdAt)}`;
        if (doc.processedAt) {
            card.querySelector('[data-field="processedBreak"]').style.display = '';
            card.querySelector('[data-field="processedDate"]').textContent = `Processed: ${formatDate(doc.processedAt)}`;
        }

        // Set status badge
        const badge = doc.status === 'Pending' ? 'warning' :
            doc.status === 'Completed' ? 'success' : 'danger';
        const statusBadge = card.querySelector('[data-field="statusBadge"]');
        statusBadge.classList.add(`bg-${badge}`);
        card.querySelector('[data-field="statusText"]').textContent = doc.status;

        // Show spinner if pending
        if (doc.status === 'Pending') {
            card.querySelector('[data-field="spinner"]').style.display = '';
        }

        // Show OCR preview if completed
        if (doc.status === 'Completed' && doc.content) {
            card.querySelector('[data-field="ocrPreview"]').style.display = '';
            const preview = doc.content.slice(0, 500);
            card.querySelector('[data-field="ocrText"]').textContent =
                preview + (doc.content.length > 500 ? '…' : '');
        }

        // Show error if failed
        if (doc.status === 'Failed' && doc.content) {
            const errorDiv = card.querySelector('[data-field="errorMessage"]');
            errorDiv.style.display = '';
            errorDiv.textContent = `OCR failed: ${doc.content}`;
        }

        // Set delete action
        card.querySelector('[data-action="delete"]').onclick = () => deleteDocument(doc.id);

        return card;
    }

    function render() {
        const container = $('documentsContainer');
        const list = Array.from(documents.values()).filter(d => {
            if (!searchQuery) return true;
            const q = searchQuery.toLowerCase();
            return d.fileName.toLowerCase().includes(q) ||
                (d.content && d.content.toLowerCase().includes(q));
        });

        container.innerHTML = '';

        if (list.length === 0) {
            const template = $('emptyStateTemplate');
            const emptyState = template.content.cloneNode(true);
            emptyState.querySelector('[data-field="emptyMessage"]').textContent =
                searchQuery ? 'No documents match your search' : 'No documents uploaded yet';
            container.appendChild(emptyState);
        } else {
            list.forEach(doc => container.appendChild(createCard(doc)));
        }
    }

    // ─── EVENT LISTENERS ──────────────────────────────────────────
    function setupUI() {
        // Restore theme preference
        const savedTheme = localStorage.getItem('theme');
        if (savedTheme === 'dark') {
            document.documentElement.setAttribute('data-bs-theme', 'dark');
            $('themeIcon').className = 'bi bi-sun';
        }

        $('searchBtn').onclick = search;
        $('searchInput').addEventListener('keypress', e => e.key === 'Enter' && search());
        $('clearSearchBtn').onclick = async () => {
            $('searchInput').value = '';
            searchQuery = '';
            await loadDocuments();
        };
        $('refreshBtn').onclick = async () => await loadDocuments();
    }

    // ─── INIT ─────────────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', async () => {
        setupUploadZone();
        setupUI();
        initSSE();
        await loadDocuments();
    });

    window.addEventListener('beforeunload', () => eventSource?.close());
})();