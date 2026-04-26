window.editorInterop = { getCursorPosition(el) { return el ? el.selectionStart : 0; }, setCursorPosition(el, pos) { if (el) { el.setSelectionRange(pos, pos); } } };

window.praetoriumDownload = function (filename, content, mimeType) {
    const blob = new Blob([content], { type: mimeType || "application/octet-stream" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(() => URL.revokeObjectURL(url), 1000);
};

