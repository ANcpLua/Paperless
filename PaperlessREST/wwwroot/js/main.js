function createAlert(message, type) {
    const alert = document.createElement('div');
    alert.className = `alert alert-${type} alert-dismissible fade show`;
    alert.role = 'alert';
    alert.innerHTML = `
        ${message}
        <button type="button" class="btn-close" data-bs-dismiss="alert" aria-label="Close"></button>
    `;
    return alert;
}

function showAlert(message, type) {
    const alertContainer = document.getElementById('alert-container');
    if (!alertContainer) return;
    const alert = createAlert(message, type);
    alertContainer.appendChild(alert);
    setTimeout(() => alert.remove(), 5000);
}

async function loadDocuments() {
    try {
        const response = await fetch('/documents');
        if (!response.ok) {
            throw new Error(`An error has occurred: ${response.status}`);
        }
        const documents = await response.json();
        const tbody = document.getElementById('documentsTableBody');
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

document.getElementById('uploadForm')?.addEventListener('submit', async function (e) {
    e.preventDefault();
    const formData = new FormData();
    const file = document.getElementById('file').files[0];
    formData.append('file', file);
    formData.append('name', document.getElementById('title').value);

    try {
        const response = await fetch('/documents/upload', {
            method: 'POST',
            body: formData
        });
        const result = await response.json();

        if (!response.ok) {
            throw new Error(result.message || result.errors?.join(', ') || 'Upload failed');
        }

        showAlert("Document uploaded successfully", "success");
        await new Promise(resolve => setTimeout(resolve, 1500));
        window.location.href = '/documents.html';
    } catch (error) {
        console.error("Error uploading document:", error);
        showAlert(error.message, "danger");
    }
});

async function deleteDocument(id) {
    if (!confirm("Are you sure you want to delete this document?")) return;

    try {
        const response = await fetch(`/documents/${id}`, {
            method: 'DELETE'
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || "Failed to delete document");
        }

        showAlert("Document deleted successfully", "success");
        loadDocuments();
    } catch (error) {
        console.error("Error deleting document:", error);
        showAlert(error.message, "danger");
    }
}

document.getElementById('searchButton')?.addEventListener('click', async function () {
    const query = document.getElementById('searchInput').value.trim();
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

        if (!result.success) {
            showAlert(result.message || "Search operation failed", "danger");
            return;
        }

        const resultsList = document.getElementById('searchResults');
        resultsList.innerHTML = '';

        if (result.documents.length === 0) {
            const noResultsItem = document.createElement('li');
            noResultsItem.className = 'list-group-item';
            noResultsItem.textContent = 'No documents found.';
            resultsList.appendChild(noResultsItem);
            return;
        }

        result.documents.forEach(doc => {
            const listItem = document.createElement('li');
            listItem.className = 'list-group-item';
            listItem.innerHTML = `
                <strong>${doc.name}</strong><br>
                Uploaded at: ${new Date(doc.dateUploaded).toLocaleString()}<br>
                <a href="/documents/${doc.id}/download" class="btn btn-sm btn-primary mt-2" download="${doc.name}">Download</a>
            `;
            resultsList.appendChild(listItem);
        });
    } catch (error) {
        console.error("Error searching documents:", error);
        showAlert("Failed to perform search", "danger");
    }
});

document.addEventListener('DOMContentLoaded', () => {
    if (document.getElementById('documentsTableBody')) {
        loadDocuments();
    }
});
