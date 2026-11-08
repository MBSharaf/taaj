// Client-side validation, role tab switching, password visibility, and API submission for login/register.
import { initPreferences } from '../core/i18n.js?v=7';

initPreferences();

document.querySelectorAll('[data-pw-toggle]').forEach((btn) => {
  btn.addEventListener('click', () => {
    const input = document.getElementById(btn.dataset.pwToggle);
    if (!input) return;
    input.type = input.type === 'password' ? 'text' : 'password';
  });
});

// Role switcher on register page
const roleTabs = document.querySelectorAll('[data-role-tab]');
const roleInput = document.getElementById('role-input');
const entityLabel = document.getElementById('entity-label');
const entityInput = document.getElementById('entityName');
const artBadge = document.getElementById('art-badge');
const artTitle = document.getElementById('art-title');
const artDesc = document.getElementById('art-desc');

function setRole(role) {
  if (!roleInput) return;
  const isLab = role === 'LaboratoryOwner' || role === 'lab';
  const normalized = isLab ? 'LaboratoryOwner' : 'Dentist';
  roleInput.value = normalized;
  roleTabs.forEach((t) => t.setAttribute('aria-selected', String(t.dataset.roleTab === normalized)));
  if (entityLabel && entityInput) {
    entityLabel.textContent = isLab ? 'اسم المعمل' : 'اسم العيادة';
    entityInput.placeholder = isLab ? 'معمل كراون تك الرقمي' : 'عيادة النور للأسنان';
  }
  if (artBadge && artTitle && artDesc) {
    artBadge.textContent = isLab ? 'لمعامل الأسنان' : 'لأطباء الأسنان';
    artTitle.textContent = isLab
      ? 'أنشئ متجرك الرقمي واستقبل طلبات الأطباء في محافظتك'
      : 'قارن المعامل واطلب حالتك الأولى خلال دقائق';
    artDesc.textContent = isLab
      ? 'إدارة متكاملة للطلبات، قائمة الأسعار الموحدة، كشوف حسابات الأطباء، ومحفظة المعمل.'
      : 'استعرض قوائم الأسعار الموحدة، اعتمد التصميم ثلاثي الأبعاد، وسدّد 20% فقط كدفعة أولى.';
  }
}

roleTabs.forEach((btn) => btn.addEventListener('click', () => setRole(btn.dataset.roleTab)));
const urlRole = new URLSearchParams(window.location.search).get('role');
if (urlRole) setRole(urlRole);

function clearErrors(form) {
  form.querySelectorAll('[data-err]').forEach((e) => { e.textContent = ''; });
  form.querySelectorAll('[aria-invalid="true"]').forEach((i) => i.removeAttribute('aria-invalid'));
  const alertBox = document.getElementById('auth-alert');
  if (alertBox) alertBox.hidden = true;
}

function setFieldError(form, name, msg) {
  const err = form.querySelector(`[data-err="${name}"]`);
  const input = form.elements[name];
  if (err) err.textContent = msg;
  if (input && input.setAttribute) input.setAttribute('aria-invalid', 'true');
}

function showAlert(msg, isSuccess = false) {
  const alertBox = document.getElementById('auth-alert');
  if (!alertBox) return;
  alertBox.className = `alert ${isSuccess ? 'alert-success' : 'alert-error'}`;
  alertBox.textContent = msg;
  alertBox.hidden = false;
}

function redirectForRole(role) {
  const returnUrl = new URLSearchParams(window.location.search).get('returnUrl');
  if (returnUrl && returnUrl.startsWith('/') && !returnUrl.startsWith('//')) {
    window.location.href = returnUrl;
    return;
  }
  if (role === 'Admin') window.location.href = '/admin-dashboard.html';
  else if (role === 'LaboratoryOwner' || role === 'LaboratoryStaff') window.location.href = '/lab-dashboard.html';
  else window.location.href = '/doctor-dashboard.html';
}

const loginForm = document.getElementById('login-form');
loginForm?.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearErrors(loginForm);
  const email = loginForm.email.value.trim();
  const password = loginForm.password.value;
  let valid = true;
  if (!email || !email.includes('@')) { setFieldError(loginForm, 'email', 'أدخل بريدًا إلكترونيًا صحيحًا.'); valid = false; }
  if (!password || password.length < 8) { setFieldError(loginForm, 'password', 'كلمة المرور 8 أحرف على الأقل.'); valid = false; }
  if (!valid) return;

  const btn = document.getElementById('login-submit');
  btn?.classList.add('is-loading');
  try {
    const res = await fetch('/api/v1/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, rememberMe: loginForm.rememberMe.checked }),
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      showAlert(data.detail || data.title || 'بيانات الدخول غير صحيحة.');
      return;
    }
    redirectForRole(data.role);
  } catch {
    showAlert('تعذّر الاتصال بالخادم. حاول مرة أخرى.');
  } finally {
    btn?.classList.remove('is-loading');
  }
});

const regForm = document.getElementById('register-form');
regForm?.addEventListener('submit', async (e) => {
  e.preventDefault();
  clearErrors(regForm);
  const payload = {
    role: regForm.role.value,
    fullName: regForm.fullName.value.trim(),
    phone: regForm.phone.value.trim(),
    entityName: regForm.entityName.value.trim(),
    governorate: regForm.governorate.value,
    email: regForm.email.value.trim(),
    password: regForm.password.value,
  };
  let valid = true;
  if (payload.fullName.length < 3) { setFieldError(regForm, 'fullName', 'أدخل الاسم الكامل.'); valid = false; }
  if (!/^01[0125]\d{8}$/.test(payload.phone)) { setFieldError(regForm, 'phone', 'أدخل رقم موبايل مصري صحيح (11 رقمًا).'); valid = false; }
  if (payload.entityName.length < 2) { setFieldError(regForm, 'entityName', 'هذا الحقل مطلوب.'); valid = false; }
  if (!payload.governorate) { setFieldError(regForm, 'governorate', 'اختر المحافظة.'); valid = false; }
  if (!payload.email.includes('@')) { setFieldError(regForm, 'email', 'أدخل بريدًا إلكترونيًا صحيحًا.'); valid = false; }
  if (payload.password.length < 8) { setFieldError(regForm, 'password', 'كلمة المرور 8 أحرف على الأقل.'); valid = false; }
  if (!regForm.acceptTerms.checked) { setFieldError(regForm, 'acceptTerms', 'يجب الموافقة على الشروط والأحكام.'); valid = false; }
  if (!valid) return;

  const btn = document.getElementById('register-submit');
  btn?.classList.add('is-loading');
  try {
    const res = await fetch('/api/v1/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      showAlert(data.detail || data.title || 'تعذّر إنشاء الحساب.');
      return;
    }
    redirectForRole(data.role || payload.role);
  } catch {
    showAlert('تعذّر الاتصال بالخادم. حاول مرة أخرى.');
  } finally {
    btn?.classList.remove('is-loading');
  }
});
