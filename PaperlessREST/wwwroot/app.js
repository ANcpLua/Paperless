'use strict';

(() => {

	const documentStore = new Map();
	let ocrStream = null;
	let summaryStream = null;
	let currentSearch = '';

	const getById = (id) => document.getElementById(id);

	const formatDateTime = (iso) =>
		iso ? new Date(iso).toLocaleString(undefined, {dateStyle: 'medium', timeStyle: 'short'}) : 'Unknown';

	const showNotification = (message, type = 'info') => {
		const template = getById('notificationTemplate');
		const clone = template.content.cloneNode(true);
		const alert = clone.querySelector('.alert');
		alert.classList.add(`alert-${type}`);
		clone.querySelector('[data-field="message"]').textContent = message;
		getById('notificationContainer').appendChild(clone);

		setTimeout(() => {
			const alerts = getById('notificationContainer').querySelectorAll('.alert');
			if (alerts.length > 0) alerts[0].remove();
		}, 2000);
	};

	function toggleTheme() {
		const html = document.documentElement;
		const theme = html.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
		html.setAttribute('data-bs-theme', theme);
		getById('themeIcon').className = theme === 'dark' ? 'bi bi-sun' : 'bi bi-moon';
		localStorage.setItem('theme', theme);
	}

	window.toggleTheme = toggleTheme;

	function connectOcrStream() {
		if (ocrStream) ocrStream.close();

		ocrStream = new EventSource('/api/v1/ocr-results');
		ocrStream.onopen = () => getById('sseStatus').style.display = 'none';
		ocrStream.onerror = () => {
			getById('sseStatus').style.display = 'block';
			ocrStream.close();
			setTimeout(connectOcrStream, 5000);
		};

		ocrStream.addEventListener('ocr-completed', async (e) => {
			console.log('OCR completed', JSON.parse(e.data));
			await fetchDocuments();
			showNotification('OCR completed', 'success');
		});

		ocrStream.addEventListener('ocr-failed', async (e) => {
			console.log('OCR failed', JSON.parse(e.data));
			await fetchDocuments();
			showNotification('OCR failed', 'danger');
		});
	}

	function connectSummaryStream() {
		if (summaryStream) summaryStream.close();

		summaryStream = new EventSource('/api/v1/events/genai');
		summaryStream.onerror = () => {
			summaryStream.close();
			setTimeout(connectSummaryStream, 10000);
		};

		summaryStream.addEventListener('genai-completed', async () => {
			await fetchDocuments();
			showNotification('Summary generated', 'success');
		});

		summaryStream.addEventListener('genai-failed', async () => {
			await fetchDocuments();
			showNotification('Summary generation failed', 'danger');
		});
	}

	function initializeUpload() {
		const dropZone = getById('uploadZone');
		const fileInput = getById('fileInput');

		dropZone.addEventListener('dragover', e => {
			e.preventDefault();
			dropZone.classList.add('border-info', 'bg-info', 'bg-opacity-10');
		});

		dropZone.addEventListener('dragleave', () => {
			dropZone.classList.remove('border-info', 'bg-info', 'bg-opacity-10');
		});

		dropZone.addEventListener('drop', e => {
			e.preventDefault();
			dropZone.classList.remove('border-info', 'bg-info', 'bg-opacity-10');
			processFiles(e.dataTransfer.files);
		});

		fileInput.addEventListener('change', e => processFiles(e.target.files));
	}

	async function processFiles(files) {
		for (const file of files) {
			if (file.type === 'application/pdf') {
				await uploadFile(file);
			} else {
				showNotification(`${file.name} is not a PDF`, 'warning');
			}
		}
	}

	async function uploadFile(file) {
		const formData = new FormData();
		formData.append('file', file);

		try {
			const response = await fetch('/api/v1/documents', {method: 'POST', body: formData});
			if (!response.ok) {
				const error = await response.json().catch(() => ({}));
				return showNotification(`Failed: ${error.detail || response.statusText}`, 'danger');
			}

			const document = await response.json();
			documentStore.set(document.id, document);
			renderDocuments();
			showNotification(`Uploaded ${file.name}`, 'success');
		} catch (err) {
			console.error(err);
			showNotification(`Error uploading ${file.name}`, 'danger');
		}
	}

	async function fetchDocuments() {
		try {
			const response = await fetch('/api/v1/documents');
			if (!response.ok) return;

			documentStore.clear();
			const docs = await response.json();
			docs.forEach(doc => documentStore.set(doc.id, doc));
			renderDocuments();
		} catch (err) {
			console.error(err);
			showNotification('Failed to load documents', 'danger');
		}
	}

	async function removeDocument(id) {
		if (!confirm('Delete this document?')) return;

		try {
			const response = await fetch(`/api/v1/documents/${id}`, {method: 'DELETE'});
			if (response.ok) {
				documentStore.delete(id);
				renderDocuments();
				showNotification('Document deleted', 'success');
			} else {
				showNotification('Delete failed', 'danger');
			}
		} catch (err) {
			console.error(err);
			showNotification('Error deleting document', 'danger');
		}
	}

	async function searchDocuments() {
		const query = getById('searchInput').value.trim();
		if (!query) {
			currentSearch = '';
			return fetchDocuments();
		}

		try {
			const response = await fetch(`/api/v1/documents/search?query=${encodeURIComponent(query)}&Limit=50`);
			if (!response.ok) return showNotification('Search failed', 'danger');

			const results = await response.json();
			documentStore.clear();
			results.forEach(doc => documentStore.set(doc.id || doc.Id, {
				...doc,
				status: doc.status || doc.Status || 'Completed'
			}));

			currentSearch = query;
			renderDocuments();
			showNotification(`Found ${results.length} result(s)`, 'info');
		} catch (err) {
			console.error(err);
			showNotification('Search failed', 'danger');
		}
	}


	function renderDocuments() {
		const container = getById('documentsContainer');
		const filtered = Array.from(documentStore.values()).filter(doc => {
			if (!currentSearch) return true;
			const search = currentSearch.toLowerCase();
			return doc.fileName.toLowerCase().includes(search) ||
				(doc.content && doc.content.toLowerCase().includes(search));
		});

		container.innerHTML = '';

		if (filtered.length === 0) {
			const empty = getById('emptyStateTemplate').content.cloneNode(true);
			empty.querySelector('[data-field="emptyMessage"]').textContent =
				currentSearch ? 'No documents match your search' : 'No documents uploaded yet';
			container.appendChild(empty);
		} else {
			filtered.forEach(doc => container.appendChild(buildDocumentCard(doc)));
		}
	}


	function buildDocumentCard(doc) {
		const template = getById('documentCardTemplate');
		const card = template.content.cloneNode(true);

		card.querySelector('[data-field="fileName"]').textContent = doc.fileName;
		card.querySelector('[data-field="uploadDate"]').textContent = `Uploaded: ${formatDateTime(doc.createdAt)}`;

		if (doc.processedAt) {
			card.querySelector('[data-field="processedBreak"]').style.display = '';
			card.querySelector('[data-field="processedDate"]').textContent = `Processed: ${formatDateTime(doc.processedAt)}`;
		}

		const statusColor = doc.status === 'Pending' ? 'warning' :
			doc.status === 'Completed' ? 'success' : 'danger';
		const badge = card.querySelector('[data-field="statusBadge"]');
		badge.classList.add(`bg-${statusColor}`);
		card.querySelector('[data-field="statusText"]').textContent = doc.status;

		if (doc.status === 'Pending') {
			card.querySelector('[data-field="spinner"]').style.display = '';
		}

		if (doc.status === 'Completed' && doc.content) {
			card.querySelector('[data-field="ocrPreview"]').style.display = '';
			card.querySelector('[data-field="ocrText"]').textContent =
				doc.content.slice(0, 500) + (doc.content.length > 500 ? '…' : '');
		}

		if (doc.summary) {
			card.querySelector('[data-field="genaiSection"]').style.display = '';
			card.querySelector('[data-field="genaiText"]').textContent = doc.summary;
		}

		card.querySelector('[data-action="delete"]').onclick = () => removeDocument(doc.id);

		return card;
	}

	document.addEventListener('DOMContentLoaded', async () => {

		if (localStorage.getItem('theme') === 'dark') {
			document.documentElement.setAttribute('data-bs-theme', 'dark');
			getById('themeIcon').className = 'bi bi-sun';
		}

		initializeUpload();
		connectOcrStream();
		connectSummaryStream();

		getById('searchBtn').onclick = searchDocuments;
		getById('searchInput').addEventListener('keypress', e => e.key === 'Enter' && searchDocuments());
		getById('clearSearchBtn').onclick = () => {
			getById('searchInput').value = '';
			currentSearch = '';
			fetchDocuments();
		};
		getById('refreshBtn').onclick = fetchDocuments;

		await fetchDocuments();
	});

	window.addEventListener('beforeunload', () => {
		ocrStream?.close();
		summaryStream?.close();
	});
})();
