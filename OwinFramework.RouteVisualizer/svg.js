function init(evt) {
    if (window.svgDocument == null) {
        svgDocument = evt.target.ownerDocument;
    }
}

function Show(evt, id) {
    var element = svgDocument.getElementById(id);
    element.setAttributeNS(null, "visibility", "visible");
}

function Hide(evt, id) {
    var element = svgDocument.getElementById(id);
    element.setAttributeNS(null, "visibility", "hidden");
}

function Navigate(url) {
    window.location = url;
}
