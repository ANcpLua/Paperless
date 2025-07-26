// ES module for Scalar dev configuration
const ScalarDevConfig = {
    init() {
        console.log('📚 Scalar Dev Config initializing...');

        // Wait a bit for Scalar to fully render
        setTimeout(() => {
            this.injectStyles();
            this.addCopyButtons();
            this.setupObservers();
            this.setupKeyboardShortcuts();
            this.showShortcutsInConsole();
        }, 1000);
    },

    injectStyles() {
        const style = document.createElement('style');
        style.textContent = `
            /* Copy button styles */
            .scalar-copy-button {
                position: absolute;
                top: 8px;
                right: 8px;
                padding: 4px 8px;
                background: var(--scalar-background-3, rgba(0,0,0,0.1));
                border: 1px solid var(--scalar-border-color, rgba(0,0,0,0.2));                             r
                border-radius: 4px;
                color: var(--scalar-color-1, #333);
                cursor: pointer;
                font-size: 14px;
                opacity: 0;
                transition: opacity 0.2s;
                z-index: 10;
            }
            
            /* Show button on hover */
            pre:hover .scalar-copy-button,
            .code-snippet:hover .scalar-copy-button,
            .hljs:hover .scalar-copy-button {
                opacity: 1;
            }
            
            /* Toast notification */
            .scalar-toast {
                position: fixed;
                bottom: 20px;
                right: 20px;
                background: var(--scalar-color-accent, #8b5cf6);
                color: white;
                padding: 12px 20px;
                border-radius: 8px;
                font-size: 14px;
                z-index: 10000;
                animation: slideIn 0.3s ease-out;
                box-shadow: 0 4px 6px rgba(0,0,0,0.1);
            }
            
            .scalar-toast.error {
                background: #dc3545;
            }
            
            @keyframes slideIn {
                from { 
                    transform: translateX(100%); 
                    opacity: 0; 
                }
                to { 
                    transform: translateX(0); 
                    opacity: 1; 
                }
            }
            
            /* Enhanced code block styling */
            .http-method {
                font-weight: bold;
                font-size: 14px;
            }
            
            /* Better request/response styling */
            .request-content pre,
            .response-content pre {
                position: relative;
            }
        `;
        document.head.appendChild(style);
    },

    addCopyButtons() {
        const addButtons = () => {
            // Find all code blocks in Scalar
            const selectors = [
                'pre:not(.has-copy-button)',
                '.code-snippet:not(.has-copy-button)',
                '.hljs:not(.has-copy-button)',
                '[class*="code"]:not(.has-copy-button) pre'
            ];

            const codeBlocks = document.querySelectorAll(selectors.join(', '));

            codeBlocks.forEach(element => {
                // Skip if it's not actually a code block
                if (!element.textContent || element.textContent.trim().length === 0) return;

                element.classList.add('has-copy-button');
                element.style.position = 'relative';

                const button = document.createElement('button');
                button.textContent = '📋';
                button.title = 'Copy to clipboard';
                button.className = 'scalar-copy-button';

                button.addEventListener('click', async (e) => {
                    e.stopPropagation();
                    e.preventDefault();

                    const code = element.textContent || '';

                    try {
                        await navigator.clipboard.writeText(code);
                        button.textContent = '✅';
                        this.showToast('Copied to clipboard!');
                        setTimeout(() => button.textContent = '📋', 2000);
                    } catch (err) {
                        console.error('Failed to copy:', err);
                        // Fallback method
                        const textArea = document.createElement('textarea');
                        textArea.value = code;
                        textArea.style.position = 'fixed';
                        textArea.style.left = '-999999px';
                        document.body.appendChild(textArea);
                        textArea.select();

                        try {
                            document.execCommand('copy');
                            button.textContent = '✅';
                            this.showToast('Copied to clipboard!');
                            setTimeout(() => button.textContent = '📋', 2000);
                        } catch (fallbackErr) {
                            console.error('Fallback copy failed:', fallbackErr);
                            this.showToast('Failed to copy', 'error');
                        }

                        document.body.removeChild(textArea);
                    }
                });

                element.appendChild(button);
            });
        };

        // Run immediately and after delays to catch dynamically added content
        addButtons();
        setTimeout(addButtons, 1000);
        setTimeout(addButtons, 2000);
    },

    setupObservers() {
        const observer = new MutationObserver(() => {
            // Debounce to avoid too many calls
            clearTimeout(this.observerTimeout);
            this.observerTimeout = setTimeout(() => {
                this.addCopyButtons();
            }, 500);
        });

        // Observe the entire document for changes
        observer.observe(document.body, {
            childList: true,
            subtree: true
        });
    },

    setupKeyboardShortcuts() {
        document.addEventListener('keydown', (e) => {
            // Only handle shortcuts when Ctrl+Shift is pressed
            if (!e.ctrlKey || !e.shiftKey) return;

            switch(e.key.toUpperCase()) {
                case 'C':
                    e.preventDefault();
                    this.copyActiveContent('response');
                    break;
                case 'R':
                    e.preventDefault();
                    this.copyActiveContent('request');
                    break;
                case 'U':
                    e.preventDefault();
                    this.copyActiveUrl();
                    break;
            }
        });
    },

    copyActiveContent(type) {
        // Try multiple selectors to find the content
        const selectors = [
            `.${type}-content pre`,
            `.${type} pre`,
            `[class*="${type}"] pre`,
            `.scalar-api-client__${type} pre`
        ];

        let content = null;
        for (const selector of selectors) {
            const element = document.querySelector(selector);
            if (element && element.textContent) {
                content = element.textContent;
                break;
            }
        }

        if (content) {
            navigator.clipboard.writeText(content);
            this.showToast(`${type.charAt(0).toUpperCase() + type.slice(1)} copied!`);
        } else {
            this.showToast(`No ${type} content found`, 'error');
        }
    },

    copyActiveUrl() {
        // Try to find URL in various possible locations
        const urlSelectors = [
            '.scalar-api-client__url input',
            '.request-url input',
            '[class*="url"] input',
            '.endpoint-url'
        ];

        let url = null;
        for (const selector of urlSelectors) {
            const element = document.querySelector(selector);
            if (element) {
                url = element.value || element.textContent;
                if (url) break;
            }
        }

        if (url) {
            navigator.clipboard.writeText(url);
            this.showToast('URL copied!');
        } else {
            this.showToast('No URL found', 'error');
        }
    },

    showToast(message, type = 'success') {
        const toast = document.createElement('div');
        toast.className = `scalar-toast ${type}`;
        toast.textContent = message;
        document.body.appendChild(toast);

        setTimeout(() => {
            toast.style.animation = 'slideOut 0.3s ease-out forwards';
            setTimeout(() => toast.remove(), 300);
        }, 3000);
    },

    showShortcutsInConsole() {
        console.log('%c⌨️ Scalar Developer Shortcuts:', 'color: #8b5cf6; font-size: 16px; font-weight: bold;');
        console.log('%c  Ctrl+Shift+C: Copy response', 'color: #6ea8fe;');
        console.log('%c  Ctrl+Shift+R: Copy request', 'color: #6ea8fe;');
        console.log('%c  Ctrl+Shift+U: Copy URL', 'color: #6ea8fe;');
        console.log('%c  Click any code block to copy', 'color: #6ea8fe;');
    },

    observerTimeout: null
};

// Export as default for ES module
export default ScalarDevConfig;

// Also try to initialize immediately if possible
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => ScalarDevConfig.init());
} else {
    ScalarDevConfig.init();
}