// Contact form: shows the "received" message (behaviour only; the text is in the HTML).
document.getElementById('contact-form')?.addEventListener('submit', (e) => {
  e.preventDefault();
  document.getElementById('contact-alert').hidden = false;
  e.target.reset();
});
