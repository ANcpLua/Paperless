/**
 * Paperless Remake - app.js
 * A modern, vanilla JS implementation for the Paperless frontend.
 *
 * FIX: Corrected initialization order and SSE event listeners to prevent
 * missed updates on the first upload.
 */

// Using a single object to encapsulate the entire application
const PaperlessApp = {
    // STATE: Centralized state management
    state: {
        documents: new Map(),
        eventSource: null,
        searchQuery: '',
        isShuttingDown: false,
        reconnectTimeout: null,
        reconnectAttempts: 0,
        activeTheme: 'system',
    },

    // CONFIG: Static configuration values
    config: {
        MAX_RECONNECT_ATTEMPTS: 5,
        RECONNECT_DELAY: 5000,
        MAX_RECONNECT_DELAY: 30000,
        NOTIFICATION_DURATION: 3500,
        OCR_PREVIEW_LENGTH: 200,
    },

    // DOM_ELEMENTS: Cache for frequently accessed DOM elements
    elements: {},

    /**
     * Initializes the entire application.
     * Entry point of the script. Now async to ensure proper startup order.
     */
    async init() {
        // Cache all necessary DOM elements for performance
        this.cacheDOMElements();

        // Set up all event listeners for user interaction
        this.bindEventListeners();

        // Initialize UI modules
        this.Theme.init();

        // Load initial data from the server BEFORE connecting to SSE
        await this.API.loadDocuments();

        // Now that we have the initial state, connect for real-time updates
        this.SSE.init();
    },

    /**
     * Caches DOM elements to avoid repeated queries.
     */
    cacheDOMElements() {
        this.elements = {
            contentArea: document.getElementById('content-area'),
            sseStatus: document.getElementById('sse-status'),
            searchInput: document.getElementById('search-input'),
            clearSearchBtn: document.getElementById('clear-search-btn'),
            themeSwitcher: document.getElementById('theme-switcher'),
            dropzoneOverlay: document.getElementById('dropzone-overlay'),
            fileInput: document.getElementById('file-input'),
            notificationContainer: document.getElementById('notification-container'),
            modal: {
                overlay: document.getElementById('modal-overlay'),
                box: document.getElementById('modal-box'),
                title: document.getElementById('modal-title'),
                message: document.getElementById('modal-message'),
                confirmBtn: document.getElementById('modal-confirm'),
                cancelBtn: document.getElementById('modal-cancel'),
            },
            // Action buttons
            uploadBtn: document.getElementById('upload-btn'),
            refreshBtn: document.getElementById('refresh-btn'),
            searchNavBtn: document.getElementById('nav-search-btn'),
        };
    },

    /**
     * Binds all application-level event listeners.
     */
    bindEventListeners() {
        // Search functionality
        this.elements.searchInput.addEventListener('input', () => this.handleSearch());
        this.elements.clearSearchBtn.addEventListener('click', () => this.clearSearch());
        this.elements.searchNavBtn.addEventListener('click', (e) => {
            e.preventDefault();
            this.elements.searchInput.focus();
        });

        // File Upload (Drag & Drop, Click)
        document.body.addEventListener('dragover', (e) => this.FileUpload.handleDragOver(e));
        document.body.addEventListener('dragleave', (e) => this.FileUpload.handleDragLeave(e));
        document.body.addEventListener('drop', (e) => this.FileUpload.handleDrop(e));
        this.elements.uploadBtn.addEventListener('click', () => this.elements.fileInput.click());
        this.elements.fileInput.addEventListener('change', (e) => this.FileUpload.processFiles(e.target.files));

        // Other actions
        this.elements.refreshBtn.addEventListener('click', async (e) => {
            e.preventDefault();
            this.Notification.show('Refreshing documents...', 'info');
            await this.API.loadDocuments();
        });

        // Page lifecycle events for SSE management
        window.addEventListener('beforeunload', () => this.SSE.shutdown());
        document.addEventListener('visibilitychange', () => {
            if (document.hidden) {
                this.SSE.shutdown();
            } else {
                this.state.isShuttingDown = false;
                this.state.reconnectAttempts = 0;
                this.SSE.init();
            }
        });
    },

    /**
     * Handles search input and triggers rendering.
     */
    handleSearch() {
        this.state.searchQuery = this.elements.searchInput.value.trim().toLowerCase();
        this.elements.clearSearchBtn.classList.toggle('visible', this.state.searchQuery.length > 0);
        this.UI.render();
    },

    /**
     * Clears the search input and re-renders the full list.
     */
    clearSearch() {
        this.elements.searchInput.value = '';
        this.handleSearch();
    },

    // MODULE: UI - Handles all rendering and DOM manipulation
    UI: {
        /**
         * Main render function for the content area.
         */
        render() {
            const parent = PaperlessApp;
            const container = parent.elements.contentArea;

            const filteredDocs = Array.from(parent.state.documents.values()).filter(doc => {
                if (!parent.state.searchQuery) return true;
                return doc.fileName.toLowerCase().includes(parent.state.searchQuery) ||
                    (doc.content && doc.content.toLowerCase().includes(parent.state.searchQuery));
            });

            // Sort documents by creation date, newest first
            filteredDocs.sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));

            if (filteredDocs.length === 0) {
                container.innerHTML = this.getEmptyStateHTML();
            } else {
                container.innerHTML = `<div class="documents-grid">${filteredDocs.map(doc => this.createDocumentCardHTML(doc)).join('')}</div>`;
                this.bindCardEventListeners();
            }
        },

        /**
         * Binds event listeners to newly rendered document cards.
         */
        bindCardEventListeners() {
            document.querySelectorAll('.card-delete-btn').forEach(button => {
                button.addEventListener('click', (e) => {
                    const docId = e.currentTarget.dataset.id;
                    const doc = PaperlessApp.state.documents.get(docId);
                    if (doc) {
                        PaperlessApp.Modal.show({
                            title: 'Delete Document?',
                            message: `Are you sure you want to permanently delete "${doc.fileName}"? This action cannot be undone.`,
                            confirmText: 'Delete',
                            onConfirm: () => PaperlessApp.API.deleteDocument(docId)
                        });
                    }
                });
            });
        },

        /**
         * Generates HTML for a single document card.
         * @param {object} doc - The document object.
         * @returns {string} HTML string for the card.
         */
        createDocumentCardHTML(doc) {
            const { formatDate, getStatusInfo } = PaperlessApp.Utils;
            const status = getStatusInfo(doc.status);

            return `
                <div class="document-card" id="doc-${doc.id}">
                    <div class="card-header">
                        <div class="card-title-group">
                            <i class="ph-fill ph-file-pdf"></i>
                            <h3 class="card-title" title="${doc.fileName}">${doc.fileName}</h3>
                        </div>
                        <button class="card-delete-btn" data-id="${doc.id}" title="Delete document">
                            <i class="ph-bold ph-trash"></i>
                        </button>
                    </div>
                    <div class="card-body">
                        <div class="card-metadata">
                            <span>Uploaded: ${formatDate(doc.createdAt)}</span>
                            ${doc.processedAt ? `<span>Processed: ${formatDate(doc.processedAt)}</span>` : ''}
                        </div>
                        ${doc.status === 'Completed' && doc.content ? `
                            <div class="ocr-preview">${doc.content.substring(0, PaperlessApp.config.OCR_PREVIEW_LENGTH)}</div>
                        ` : ''}
                         ${doc.status === 'Failed' && doc.content ? `
                            <div class="ocr-preview" style="color: var(--accent-danger);">${doc.content}</div>
                        ` : ''}
                    </div>
                    <div class="card-footer">
                        <div class="status-badge ${status.class}">
                            ${status.icon}
                            <span>${doc.status}</span>
                        </div>
                    </div>
                </div>
            `;
        },

        /**
         * Generates HTML for the empty state (no documents or no search results).
         * @returns {string} HTML string for the empty state.
         */
        getEmptyStateHTML() {
            const isSearching = PaperlessApp.state.searchQuery.length > 0;
            return `
                <div class="empty-state">
                    <i class="ph-bold ${isSearching ? 'ph-magnifying-glass' : 'ph-files'}"></i>
                    <h2>${isSearching ? 'No documents found' : 'No documents yet'}</h2>
                    <p>${isSearching ? 'Try a different search term or clear the search.' : 'Upload your first PDF to get started.'}</p>
                </div>
            `;
        },

        /**
         * Updates the SSE connection status indicator in the UI.
         * @param {boolean} isConnected - The connection status.
         */
        setConnectionStatus(isConnected) {
            PaperlessApp.elements.sseStatus.classList.toggle('connected', isConnected);
        }
    },

    // MODULE: API - Handles all communication with the backend
    API: {
        async _fetch(url, options = {}) {
            try {
                const response = await fetch(url, options);
                if (!response.ok) {
                    const problem = await response.json().catch(() => ({}));
                    throw new Error(problem.detail || `HTTP error! status: ${response.status}`);
                }
                return response;
            } catch (error) {
                console.error(`Fetch error for ${url}:`, error);
                throw error;
            }
        },

        async loadDocuments() {
            try {
                const response = await this._fetch('/api/v1/documents');
                const docs = await response.json();
                PaperlessApp.state.documents.clear();
                docs.forEach(doc => {
                    const normalized = PaperlessApp.Utils.normalizeDocument(doc);
                    PaperlessApp.state.documents.set(normalized.id, normalized);
                });
                PaperlessApp.UI.render();
            } catch (error) {
                PaperlessApp.Notification.show(`Failed to load documents: ${error.message}`, 'danger');
            }
        },

        async uploadFile(file) {
            const formData = new FormData();
            formData.append('file', file);
            try {
                // We don't need the response, SSE will notify us.
                await this._fetch('/api/v1/documents', { method: 'POST', body: formData });
                PaperlessApp.Notification.show(`Uploading "${file.name}"...`, 'info');
                // Optimistically add a pending card to the UI
                const tempId = `temp-${Date.now()}`;
                const pendingDoc = {
                    id: tempId,
                    fileName: file.name,
                    status: 'Pending',
                    content: '',
                    createdAt: new Date().toISOString(),
                    processedAt: null
                };
                PaperlessApp.state.documents.set(tempId, pendingDoc);
                PaperlessApp.UI.render();

            } catch (error) {
                PaperlessApp.Notification.show(`Upload failed for "${file.name}": ${error.message}`, 'danger');
            }
        },

        async deleteDocument(id) {
            try {
                await this._fetch(`/api/v1/documents/${id}`, { method: 'DELETE' });
                PaperlessApp.state.documents.delete(id);
                PaperlessApp.UI.render();
                PaperlessApp.Notification.show('Document deleted successfully.', 'success');
            } catch (error) {
                PaperlessApp.Notification.show(`Failed to delete document: ${error.message}`, 'danger');
            }
        }
    },

    // MODULE: SSE - Manages Server-Sent Events connection
    SSE: {
        init() {
            const parent = PaperlessApp;
            if (parent.state.isShuttingDown || parent.state.eventSource) return;
            this.cleanup();

            parent.state.eventSource = new EventSource('/api/v1/ocr-results');

            parent.state.eventSource.onopen = () => {
                console.log('SSE connection established.');
                parent.UI.setConnectionStatus(true);
                parent.state.reconnectAttempts = 0;
            };

            parent.state.eventSource.onerror = () => this.handleError();

            // FIX: Listen for the correct backend events
            const eventsToListen = ['ocr-completed', 'ocr-failed', 'ocr-pending'];
            eventsToListen.forEach(eventName => {
                parent.state.eventSource.addEventListener(eventName, (e) => this.handleOcrEvent(e));
            });
        },

        /**
         * Shared handler for all OCR-related server events.
         * @param {Event} e - The SSE message event.
         */
        handleOcrEvent(e) {
            const parent = PaperlessApp;
            const updatedDoc = parent.Utils.normalizeDocument(JSON.parse(e.data));

            // Remove any temporary card that might exist for this document
            const tempCard = Array.from(parent.state.documents.values()).find(d => d.status === 'Pending' && d.fileName === updatedDoc.fileName);
            if(tempCard) {
                parent.state.documents.delete(tempCard.id);
            }

            parent.state.documents.set(updatedDoc.id, updatedDoc);
            parent.UI.render();

            if (updatedDoc.status !== 'Pending') {
                const message = updatedDoc.status === 'Completed' ? 'processed successfully' : 'failed to process';
                const type = updatedDoc.status === 'Completed' ? 'success' : 'danger';
                parent.Notification.show(`"${updatedDoc.fileName}" ${message}.`, type);
            }
        },

        handleError() {
            const parent = PaperlessApp;
            this.cleanup();
            if (parent.state.isShuttingDown) return;

            parent.UI.setConnectionStatus(false);
            parent.state.reconnectAttempts++;

            if (parent.state.reconnectAttempts > parent.config.MAX_RECONNECT_ATTEMPTS) {
                console.error('Max SSE reconnection attempts reached.');
                parent.Notification.show('Connection to server lost. Please refresh.', 'danger');
                return;
            }

            const delay = Math.min(parent.config.RECONNECT_DELAY * (2 ** parent.state.reconnectAttempts), parent.config.MAX_RECONNECT_DELAY);
            parent.state.reconnectTimeout = setTimeout(() => this.init(), delay);
        },

        cleanup() {
            const parent = PaperlessApp;
            if (parent.state.reconnectTimeout) clearTimeout(parent.state.reconnectTimeout);
            if (parent.state.eventSource) {
                parent.state.eventSource.close();
                parent.state.eventSource = null;
            }
        },

        shutdown() {
            PaperlessApp.state.isShuttingDown = true;
            this.cleanup();
        }
    },

    // MODULE: Theme - Manages light/dark/system theme switching
    Theme: {
        init() {
            const parent = PaperlessApp;
            parent.elements.themeSwitcher.addEventListener('click', (e) => {
                const target = e.target.closest('button');
                if (target) this.set(target.dataset.theme);
            });

            const savedTheme = localStorage.getItem('paperless-theme') || 'system';
            this.set(savedTheme);

            window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
                if (parent.state.activeTheme === 'system') this.apply();
            });
        },

        set(theme) {
            PaperlessApp.state.activeTheme = theme;
            localStorage.setItem('paperless-theme', theme);
            this.apply();
            this.updateSwitcherUI();
        },

        apply() {
            const theme = PaperlessApp.state.activeTheme;
            const isDark = (theme === 'dark') || (theme === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
            document.documentElement.classList.toggle('dark', isDark);
        },

        updateSwitcherUI() {
            const { themeSwitcher } = PaperlessApp.elements;
            themeSwitcher.querySelectorAll('button').forEach(btn => {
                btn.classList.toggle('active', btn.dataset.theme === PaperlessApp.state.activeTheme);
            });
        }
    },

    // MODULE: FileUpload - Handles drag & drop and file input
    FileUpload: {
        handleDragOver(e) {
            e.preventDefault();
            PaperlessApp.elements.dropzoneOverlay.classList.add('visible');
        },
        handleDragLeave(e) {
            if (e.relatedTarget === null || !e.currentTarget.contains(e.relatedTarget)) {
                PaperlessApp.elements.dropzoneOverlay.classList.remove('visible');
            }
        },
        handleDrop(e) {
            e.preventDefault();
            PaperlessApp.elements.dropzoneOverlay.classList.remove('visible');
            this.processFiles(e.dataTransfer.files);
        },
        async processFiles(files) {
            for (const file of files) {
                if (file.type === 'application/pdf') {
                    await PaperlessApp.API.uploadFile(file);
                } else {
                    PaperlessApp.Notification.show(`"${file.name}" is not a PDF.`, 'warning');
                }
            }
        }
    },

    // MODULE: Notification - Displays temporary messages
    Notification: {
        show(message, type = 'info') {
            const container = PaperlessApp.elements.notificationContainer;
            const notification = document.createElement('div');
            notification.className = `notification ${type}`;

            const icons = {
                info: 'ph-info',
                success: 'ph-check-circle',
                warning: 'ph-warning',
                danger: 'ph-x-circle',
            };

            notification.innerHTML = `<i class="ph-fill ${icons[type]}"></i><p>${message}</p>`;
            container.appendChild(notification);

            setTimeout(() => {
                notification.remove();
            }, PaperlessApp.config.NOTIFICATION_DURATION);
        }
    },

    // MODULE: Modal - Handles confirmation dialogs
    Modal: {
        show({ title, message, confirmText = 'Confirm', onConfirm }) {
            const { modal } = PaperlessApp.elements;
            modal.title.textContent = title;
            modal.message.textContent = message;
            modal.confirmBtn.textContent = confirmText;

            modal.overlay.classList.add('visible');

            const confirmHandler = () => {
                onConfirm();
                this.hide();
                cleanup();
            };

            const cancelHandler = () => {
                this.hide();
                cleanup();
            };

            const cleanup = () => {
                modal.confirmBtn.removeEventListener('click', confirmHandler);
                modal.cancelBtn.removeEventListener('click', cancelHandler);
            };

            modal.confirmBtn.addEventListener('click', confirmHandler);
            modal.cancelBtn.addEventListener('click', cancelHandler);
        },
        hide() {
            PaperlessApp.elements.modal.overlay.classList.remove('visible');
        }
    },

    // UTILS: Helper functions
    Utils: {
        formatDate(dateString) {
            return dateString ? new Date(dateString).toLocaleString(undefined, {
                dateStyle: 'medium',
                timeStyle: 'short'
            }) : 'N/A';
        },
        normalizeDocument(doc) {
            return {
                id: doc.id || doc.Id,
                fileName: doc.fileName || doc.FileName,
                status: doc.status || doc.Status || 'Completed',
                content: doc.content || doc.Content || '',
                createdAt: doc.createdAt || doc.CreatedAt,
                processedAt: doc.processedAt || doc.ProcessedAt,
            };
        },
        getStatusInfo(status) {
            switch (status) {
                case 'Completed': return { class: 'completed', icon: '<i class="ph-fill ph-check-circle"></i>' };
                case 'Pending': return { class: 'pending', icon: '<div class="spinner"></div>' };
                case 'Failed': return { class: 'failed', icon: '<i class="ph-fill ph-x-circle"></i>' };
                default: return { class: 'secondary', icon: '<i class="ph-fill ph-question"></i>' };
            }
        }
    }
};

// Start the application once the DOM is fully loaded
document.addEventListener('DOMContentLoaded', () => PaperlessApp.init());
