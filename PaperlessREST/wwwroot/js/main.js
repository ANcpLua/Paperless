async function safeJsonParse(response) {
    // Try to parse JSON; if it fails, return an empty object for a fallback
    try {
        return await response.json();
    } catch {
        return {};
    }
}

function showAlert(message, type) {
    const alertContainer = document.getElementById('alert-container');
    if (!alertContainer) return;

    // Clear existing alerts to prevent stacking
    alertContainer.querySelectorAll('.alert').forEach(alert => alert.remove());

    // Create and insert alert
    const alert = document.createElement('div');
    alert.className = `alert alert-${type} alert-dismissible fade show`;
    alert.role = 'alert';
    alert.innerHTML = `
    ${message}
    <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
  `;
    alertContainer.appendChild(alert);

    // Automatic cleanup with animation
    setTimeout(() => {
        alert.classList.add('fade-out');
        setTimeout(() => alert.remove(), 300);
    }, 4700);
}

async function loadDocuments() {
    try {
        const response = await fetch('/documents');
        if (!response.ok) {
            throw new Error(`An error has occurred: ${response.status}`);
        }

        const documents = await response.json();
        const tbody = document.getElementById('documentsTableBody');
        if (!tbody) return;

        tbody.innerHTML = '';
        documents.forEach(doc => {
            const row = document.createElement('tr');
            row.innerHTML = `
        <td>${doc.id}</td>
        <td>${doc.name}</td>
        <td>${new Date(doc.dateUploaded).toLocaleString()}</td>
        <td class="document-actions">
          <a href="/documents/${doc.id}/download" class="btn btn-sm btn-primary" download="${doc.name}">Download</a>
          <button onclick="deleteDocument(${doc.id})" class="btn btn-sm btn-danger">Delete</button>
        </td>
      `;
            tbody.appendChild(row);
        });
    } catch (error) {
        console.error("Error loading documents:", error);
        showAlert("Failed to load documents", "danger");
    }
}

async function deleteDocument(id) {
    if (!confirm("Are you sure you want to delete this document?")) return;

    try {
        const response = await fetch(`/documents/${id}`, { method: 'DELETE' });
        if (!response.ok) {
            // Attempt to parse JSON error message
            const errorData = await safeJsonParse(response);
            const errorDetail = errorData.message || "Failed to delete document";
            throw new Error(errorDetail);
        }

        showAlert("Document deleted successfully", "success");
        loadDocuments();
    } catch (error) {
        console.error("Error deleting document:", error);
        showAlert(error.message, "danger");
    }
}

async function performSearch() {
    const searchInput = document.getElementById('searchInput');
    const resultsList = document.getElementById('searchResults');
    if (!searchInput || !resultsList) return;

    const query = searchInput.value.trim();
    if (!query) {
        showAlert("Please enter a search term", "warning");
        return;
    }

    try {
        const response = await fetch(`/documents/search?query=${encodeURIComponent(query)}`);
        if (!response.ok) {
            throw new Error(`An error has occurred: ${response.status}`);
        }

        const result = await response.json();
        resultsList.innerHTML = '';

        if (!result.documents?.length) {
            resultsList.innerHTML = '<li class="list-group-item">No documents found.</li>';
            return;
        }

        result.documents.forEach(doc => {
            resultsList.innerHTML += `
        <li class="list-group-item">
          <strong>${doc.name}</strong><br>
          Uploaded at: ${new Date(doc.dateUploaded).toLocaleString()}<br>
          <a href="/documents/${doc.id}/download" class="btn btn-sm btn-primary mt-2" download="${doc.name}">Download</a>
        </li>
      `;
        });
    } catch (error) {
        console.error("Error searching documents:", error);
        showAlert("Failed to perform search", "danger");
    }
}

function handleUpload(e) {
    e.preventDefault();
    const fileInput = document.getElementById('file');
    const file = fileInput?.files[0];

    if (!file) {
        showAlert("Please select a file first.", "warning");
        return;
    }

    // Check if the file is PDF
    const isPDF = file.type === 'application/pdf' || /\.pdf$/i.test(file.name);
    if (!isPDF) {
        showAlert("Only PDF files are allowed. Please upload a PDF file.", "warning");
        fileInput.value = ""; // Clear file input
        return;
    }

    const formData = new FormData();
    formData.append('file', file);
    formData.append('name', file.name);

    fetch('/documents/upload', {
        method: 'POST',
        body: formData
    })
        .then(async response => {
            if (!response.ok) {
                const result = await safeJsonParse(response);
                const errorMsg = result.message || result.errors?.join(', ') || 'Upload failed';
                throw new Error(errorMsg);
            }
            try {
                return await response.json();
            } catch {
                throw new Error("Upload succeeded, but the server response was unreadable.");
            }
        })
        .then(() => {
            showAlert("Document uploaded successfully", "success");
            setTimeout(() => (window.location.href = '/documents.html'), 1500);
        })
        .catch(error => {
            console.error("Error uploading document:", error);
            showAlert(error.message, "danger");
        });
}

document.addEventListener('DOMContentLoaded', () => {
    // If the documents table exists, load documents
    if (document.getElementById('documentsTableBody')) {
        loadDocuments();
    }

    // Search event listeners
    const searchButton = document.getElementById('searchButton');
    const searchInput = document.getElementById('searchInput');
    if (searchButton) {
        searchButton.addEventListener('click', performSearch);
    }
    if (searchInput) {
        searchInput.addEventListener('keypress', e => {
            if (e.key === 'Enter') {
                e.preventDefault();
                performSearch();
            }
        });
    }

    // Upload event listener
    const uploadForm = document.getElementById('uploadForm');
    if (uploadForm) {
        uploadForm.addEventListener('submit', handleUpload);
    }
});
