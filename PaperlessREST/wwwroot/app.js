// Simple state management
const state = {
    documents: new Map(),
    searchQuery: '',
    isDark: false
};

// Simple bubble system
const BubbleGame = {
    bubbles: [],
    colors: ['#ff6b6b', '#4ecdc4', '#45b7d1', '#96ceb4', '#feca57', '#ff9ff3'],

    init() {
        console.log('🎮 Starting bubble game');
        this.container = document.getElementById('bubbleContainer');
        if (!this.container) return;

        this.container.innerHTML = '';
        this.createBubbles();
        this.startLoop();
    },

    createBubbles() {
        for (let i = 0; i < 6; i++) {
            setTimeout(() => this.addBubble(), i * 300);
        }
    },

    addBubble() {
        const bubble = document.createElement('div');
        bubble.className = 'bubble';

        const size = 40 + Math.random() * 60;
        const x = Math.random() * (window.innerWidth - size);
        const y = Math.random() * (window.innerHeight - size);
        const color = this.colors[Math.floor(Math.random() * this.colors.length)];

        bubble.style.cssText = `
            width: ${size}px;
            height: ${size}px;
            left: ${x}px;
            top: ${y}px;
            background: radial-gradient(circle at 30% 30%, 
                rgba(255,255,255,0.6), 
                ${color}40 40%, 
                ${color}20
            );
        `;

        // Click handler
        bubble.addEventListener('click', (e) => {
            console.log('💥 Bubble popped!');
            this.popBubble(bubble, e.clientX, e.clientY, color);
        });

        this.container.appendChild(bubble);
        this.bubbles.push(bubble);

        // Auto remove after 15 seconds
        setTimeout(() => {
            if (bubble.parentNode) {
                this.removeBubble(bubble);
            }
        }, 15000);
    },

    popBubble(bubble, x, y, color) {
        // Disable bubble
        bubble.style.pointerEvents = 'none';
        bubble.style.opacity = '0.5';

        // Create explosion
        this.createExplosion(x, y, color);

        // Remove bubble
        setTimeout(() => this.removeBubble(bubble), 200);

        // Add new bubble
        setTimeout(() => this.addBubble(), 1000);
    },

    createExplosion(x, y, color) {
        console.log('💥 Creating explosion');

        for (let i = 0; i < 8; i++) {
            const particle = document.createElement('div');
            particle.className = 'particle';

            const angle = (i / 8) * Math.PI * 2;
            const distance = 50 + Math.random() * 30;
            const endX = x + Math.cos(angle) * distance;
            const endY = y + Math.sin(angle) * distance;

            particle.style.cssText = `
                left: ${x}px;
                top: ${y}px;
                background: ${color};
                transition: all 0.8s ease-out;
            `;

            document.body.appendChild(particle);

            // Animate
            requestAnimationFrame(() => {
                particle.style.left = endX + 'px';
                particle.style.top = endY + 'px';
                particle.style.opacity = '0';
                particle.style.transform = 'scale(0)';
            });

            // Remove
            setTimeout(() => {
                if (particle.parentNode) {
                    particle.parentNode.removeChild(particle);
                }
            }, 800);
        }
    },

    removeBubble(bubble) {
        const index = this.bubbles.indexOf(bubble);
        if (index > -1) {
            this.bubbles.splice(index, 1);
        }
        if (bubble.parentNode) {
            bubble.parentNode.removeChild(bubble);
        }
    },

    startLoop() {
        setInterval(() => {
            if (this.bubbles.length < 5) {
                this.addBubble();
            }
        }, 3000);
    }
};

// Notification system
const Notifications = {
    show(message, type = 'info') {
        const container = document.getElementById('notifications');
        const notification = document.createElement('div');
        notification.className = `notification ${type}`;
        notification.innerHTML = `
            <div style="display: flex; align-items: center; gap: 0.5rem;">
                <i class="bi bi-${this.getIcon(type)}"></i>
                <span>${message}</span>
            </div>
        `;

        container.appendChild(notification);

        // Show
        requestAnimationFrame(() => {
            notification.classList.add('show');
        });

        // Auto hide
        setTimeout(() => {
            notification.classList.remove('show');
            setTimeout(() => {
                if (notification.parentNode) {
                    notification.parentNode.removeChild(notification);
                }
            }, 300);
        }, 4000);
    },

    getIcon(type) {
        const icons = {
            success: 'check-circle',
            error: 'exclamation-triangle',
            info: 'info-circle'
        };
        return icons[type] || 'info-circle';
    }
};

// API functions
const API = {
    async uploadFile(file) {
        const formData = new FormData();
        formData.append('file', file);

        try {
            const response = await fetch('/api/v1/documents', {
                method: 'POST',
                body: formData
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const doc = await response.json();
            state.documents.set(doc.id, doc);

            Notifications.show(`Uploaded "${file.name}"`, 'success');
            this.renderDocuments();
        } catch (error) {
            Notifications.show(`Failed to upload "${file.name}"`, 'error');
            console.error('Upload failed:', error);
        }
    },

    async loadDocuments() {
        try {
            const response = await fetch('/api/v1/documents');
            const docs = await response.json();

            state.documents.clear();
            docs.forEach(doc => state.documents.set(doc.id, doc));

            this.renderDocuments();
        } catch (error) {
            Notifications.show('Failed to load documents', 'error');
            console.error('Load failed:', error);
        }
    },

    async deleteDocument(id) {
        try {
            const response = await fetch(`/api/v1/documents/${id}`, {
                method: 'DELETE'
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            state.documents.delete(id);
            Notifications.show('Document deleted', 'success');
            this.renderDocuments();
        } catch (error) {
            Notifications.show('Failed to delete document', 'error');
            console.error('Delete failed:', error);
        }
    },

    renderDocuments() {
        const container = document.getElementById('documentsContainer');
        const docs = Array.from(state.documents.values())
            .filter(doc => {
                if (!state.searchQuery) return true;
                const query = state.searchQuery.toLowerCase();
                return doc.fileName?.toLowerCase().includes(query) ||
                    doc.content?.toLowerCase().includes(query);
            })
            .sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));

        if (docs.length === 0) {
            container.innerHTML = `
                <div class="empty-state">
                    <i class="bi bi-inbox"></i>
                    <h3>No documents found</h3>
                    <p>Upload a PDF to get started</p>
                </div>
            `;
            return;
        }

        container.innerHTML = docs.map(doc => `
            <div class="document-card">
                <div class="card-header">
                    <div class="card-title">
                        <div class="file-icon">
                            <i class="bi bi-file-pdf"></i>
                        </div>
                        <div>
                            <h3>${doc.fileName}</h3>
                            <p style="color: var(--text-light); font-size: 0.875rem;">
                                ${new Date(doc.createdAt).toLocaleDateString()}
                            </p>
                        </div>
                    </div>
                    <button class="delete-btn" onclick="API.deleteDocument('${doc.id}')">
                        <i class="bi bi-trash"></i>
                    </button>
                </div>
                ${doc.content ? `
                    <div style="margin-top: 1rem; padding-top: 1rem; border-top: 1px solid var(--border);">
                        <h4 style="font-size: 0.875rem; margin-bottom: 0.5rem;">Content Preview</h4>
                        <div style="background: var(--bg); padding: 1rem; border-radius: 8px; font-size: 0.875rem; max-height: 100px; overflow: hidden;">
                            ${doc.content.substring(0, 200)}${doc.content.length > 200 ? '...' : ''}
                        </div>
                    </div>
                ` : ''}
            </div>
        `).join('');
    }
};

// File handling
const FileHandler = {
    init() {
        const fileInput = document.getElementById('fileInput');
        const uploadZone = document.getElementById('uploadZone');

        fileInput.addEventListener('change', (e) => {
            this.handleFiles(Array.from(e.target.files));
        });

        // Drag and drop
        uploadZone.addEventListener('dragover', (e) => {
            e.preventDefault();
            uploadZone.style.borderColor = 'var(--primary)';
        });

        uploadZone.addEventListener('dragleave', () => {
            uploadZone.style.borderColor = 'var(--border)';
        });

        uploadZone.addEventListener('drop', (e) => {
            e.preventDefault();
            uploadZone.style.borderColor = 'var(--border)';
            this.handleFiles(Array.from(e.dataTransfer.files));
        });

        uploadZone.addEventListener('click', () => {
            fileInput.click();
        });
    },

    handleFiles(files) {
        const pdfFiles = files.filter(file => file.type === 'application/pdf');
        const otherFiles = files.filter(file => file.type !== 'application/pdf');

        if (otherFiles.length > 0) {
            Notifications.show('Only PDF files are supported', 'error');
        }

        pdfFiles.forEach(file => API.uploadFile(file));
    }
};

// Theme toggle
const ThemeManager = {
    init() {
        const toggle = document.getElementById('themeToggle');
        const saved = localStorage.getItem('darkMode');

        if (saved === 'true') {
            this.setDark(true);
        }

        toggle.addEventListener('click', () => {
            this.toggle();
        });
    },

    toggle() {
        state.isDark = !state.isDark;
        this.setDark(state.isDark);
        localStorage.setItem('darkMode', state.isDark.toString());
    },

    setDark(isDark) {
        state.isDark = isDark;
        document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light');
        const toggle = document.getElementById('themeToggle');
        toggle.innerHTML = `<i class="bi bi-${isDark ? 'moon' : 'sun'}"></i>`;
    }
};

// Search functionality
const Search = {
    init() {
        const input = document.getElementById('searchInput');
        const searchBtn = document.getElementById('searchBtn');
        const clearBtn = document.getElementById('clearBtn');

        input.addEventListener('input', (e) => {
            state.searchQuery = e.target.value;
            API.renderDocuments();
        });

        searchBtn.addEventListener('click', () => {
            API.renderDocuments();
        });

        clearBtn.addEventListener('click', () => {
            input.value = '';
            state.searchQuery = '';
            API.renderDocuments();
        });
    }
};

// App initialization
const App = {
    async init() {
        console.log('🚀 Starting Paperless OCR System');

        // Initialize all modules
        ThemeManager.init();
        FileHandler.init();
        Search.init();

        // Setup refresh button
        document.getElementById('refreshBtn').addEventListener('click', () => {
            API.loadDocuments();
        });

        // Load initial data
        await API.loadDocuments();

        // Start bubble game
        setTimeout(() => {
            BubbleGame.init();
        }, 500);

        console.log('✅ App initialized');
    }
};

// Start the app
document.addEventListener('DOMContentLoaded', () => {
    App.init().catch(console.error);
});

// Export for debugging
window.BubbleGame = BubbleGame;
window.API = API;