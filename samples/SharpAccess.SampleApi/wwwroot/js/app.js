const state = {
  token: null,
  me: null,
  status: null,
  activeTenant: null,
  snackbarTimer: null
};

const elements = {
  boot: document.querySelector('#boot'),
  login: document.querySelector('#login-view'),
  app: document.querySelector('#app-view'),
  content: document.querySelector('#content'),
  drawer: document.querySelector('#drawer'),
  loginForm: document.querySelector('#login-form'),
  loginEmail: document.querySelector('#login-email'),
  loginPassword: document.querySelector('#login-password'),
  loginError: document.querySelector('#login-error'),
  oidc: document.querySelector('#oidc-options'),
  accounts: document.querySelector('#account-list'),
  context: document.querySelector('#context-chip'),
  userEmail: document.querySelector('#user-email'),
  dialog: document.querySelector('#form-dialog'),
  dialogTitle: document.querySelector('#dialog-title'),
  dialogBody: document.querySelector('#dialog-body'),
  dialogSubmit: document.querySelector('#dialog-submit'),
  snackbar: document.querySelector('#snackbar')
};

class HttpError extends Error {
  constructor(status, title, detail, traceId) {
    super(detail || title || `HTTP ${status}`);
    this.status = status;
    this.title = title || 'Request failed';
    this.detail = detail || 'The request could not be completed.';
    this.traceId = traceId || null;
  }
}

async function request(path, options = {}) {
  const headers = new Headers(options.headers || {});
  if (state.token) headers.set('Authorization', `Bearer ${state.token}`);
  if (options.body && !(options.body instanceof FormData) && typeof options.body !== 'string') {
    headers.set('Content-Type', 'application/json');
    options.body = JSON.stringify(options.body);
  }
  const response = await fetch(path, { credentials: 'same-origin', ...options, headers });
  if (response.status === 204) return null;
  const contentType = response.headers.get('content-type') || '';
  const payload = contentType.includes('json') ? await response.json() : await response.text();
  if (!response.ok) {
    const problem = typeof payload === 'object' && payload ? payload : {};
    throw new HttpError(response.status, problem.title, problem.detail || String(payload || ''), problem.traceId);
  }
  return payload;
}

async function boot() {
  bindGlobalEvents();
  applyStoredTheme();
  try {
    state.status = await request('/sample/status');
    renderAccountHints();
    renderOidcOptions();
    try {
      const session = await request('/auth/refresh', { method: 'POST' });
      if (session?.accessToken) {
        state.token = session.accessToken;
        await loadIdentity();
        showApplication();
        route(location.pathname === '/' ? '/dashboard' : location.pathname, true);
        return;
      }
    } catch (error) {
      if (!(error instanceof HttpError) || error.status !== 401) throw error;
    }
    showLogin();
  } catch (error) {
    elements.boot.classList.add('hidden');
    elements.login.classList.add('hidden');
    elements.app.classList.remove('hidden');
    renderError(error instanceof HttpError ? error.status : 500, 'Startup failed', error.message);
  }
}

function bindGlobalEvents() {
  document.addEventListener('click', event => {
    const link = event.target.closest('[data-link]');
    if (link) {
      event.preventDefault();
      route(new URL(link.href, location.origin).pathname);
      return;
    }
    const action = event.target.closest('[data-action]')?.dataset.action;
    if (action) handleAction(action, event.target.closest('[data-action]')).catch(showUnhandled);
  });
  elements.loginForm.addEventListener('submit', login);
  document.querySelector('#menu-button').addEventListener('click', () => elements.drawer.classList.toggle('open'));
  document.querySelector('#theme-button').addEventListener('click', toggleTheme);
  document.querySelector('#logout-button').addEventListener('click', logout);
  document.querySelector('#account-button').addEventListener('click', () => route('/settings'));
  window.addEventListener('popstate', () => renderRoute(location.pathname));
}

function showLogin() {
  elements.boot.classList.add('hidden');
  elements.app.classList.add('hidden');
  elements.login.classList.remove('hidden');
  elements.loginEmail.value = state.status?.accounts?.administrator || '';
  elements.loginPassword.focus();
}

function showApplication() {
  elements.boot.classList.add('hidden');
  elements.login.classList.add('hidden');
  elements.app.classList.remove('hidden');
  elements.userEmail.textContent = state.me?.email || '';
  updateContext();
}

async function login(event) {
  event.preventDefault();
  elements.loginError.textContent = '';
  const button = elements.loginForm.querySelector('button[type="submit"]');
  button.disabled = true;
  try {
    const session = await request('/auth/login', {
      method: 'POST',
      body: {
        email: elements.loginEmail.value.trim(),
        password: elements.loginPassword.value,
        tenantId: null
      }
    });
    state.token = session.accessToken;
    elements.loginPassword.value = '';
    await loadIdentity();
    showApplication();
    route('/dashboard');
  } catch (error) {
    elements.loginError.textContent = describe(error);
  } finally {
    button.disabled = false;
  }
}

async function loadIdentity() {
  state.me = await request('/auth/me');
  state.activeTenant = state.me.tenantId || null;
  elements.userEmail.textContent = state.me.email;
  updateContext();
}

function updateContext() {
  elements.context.textContent = state.activeTenant ? `Tenant ${state.activeTenant}` : 'Global context';
}

async function logout() {
  try { await request('/auth/logout', { method: 'POST' }); } catch { }
  state.token = null;
  state.me = null;
  state.activeTenant = null;
  history.replaceState({}, '', '/');
  showLogin();
}

function route(path, replace = false) {
  if (!state.me) return showLogin();
  if (replace) history.replaceState({}, '', path);
  else history.pushState({}, '', path);
  elements.drawer.classList.remove('open');
  renderRoute(path).catch(showUnhandled);
}

async function renderRoute(path) {
  const routeName = path.split('/').filter(Boolean)[0] || 'dashboard';
  document.querySelectorAll('[data-route]').forEach(link => link.classList.toggle('active', link.dataset.route === routeName));
  const routes = {
    dashboard: renderDashboard,
    users: renderUsers,
    tenants: renderTenants,
    roles: renderRoles,
    permissions: renderPermissions,
    modules: renderModules,
    audit: renderAudit,
    settings: renderSettings
  };
  const renderer = routes[routeName];
  if (!renderer) {
    renderError(404, 'Page not found', 'The requested sample-console section does not exist.');
    return;
  }
  elements.content.innerHTML = loadingMarkup();
  elements.content.focus();
  try {
    await renderer();
  } catch (error) {
    if (error instanceof HttpError) renderError(error.status, error.title, error.detail, error.traceId);
    else renderError(500, 'Unexpected error', error.message);
  }
}

async function renderDashboard() {
  const [users, tenants, roles, modules] = await Promise.all([
    optionalPage('/admin/users?limit=200'),
    optionalPage('/tenants?limit=200'),
    optionalPage('/admin/roles?limit=200'),
    optionalRequest('/sample/modules')
  ]);
  elements.content.innerHTML = `
    ${pageHeader('Dashboard', 'Exercise SharpAccess authentication, global authorization, tenancy, and module access from one local console.')}
    <section class="grid cards">
      ${metricCard(users?.length ?? '—', 'Users')}
      ${metricCard(tenants?.length ?? '—', 'Tenants')}
      ${metricCard(roles?.length ?? '—', 'Global roles')}
      ${metricCard(modules?.items?.filter(item => item.granted).length ?? '—', 'Granted modules')}
    </section>
    <section class="card" style="margin-top:18px">
      <h2>Current identity</h2>
      <div class="permission-list">
        <span class="chip">${escapeHtml(state.me.email)}</span>
        ${(state.me.globalRoles || []).map(value => `<span class="chip">Role: ${escapeHtml(value)}</span>`).join('')}
        ${(state.me.globalPermissions || []).map(value => `<span class="chip">${escapeHtml(value)}</span>`).join('')}
      </div>
    </section>`;
}

async function renderUsers() {
  const [users, roles] = await Promise.all([page('/admin/users?limit=200'), page('/admin/roles?limit=200')]);
  elements.content.innerHTML = `
    ${pageHeader('Users', 'Activate accounts and assign or remove global roles.', '<button class="button tonal" data-action="refresh">Refresh</button>')}
    <div class="table-wrap"><table><thead><tr><th>Email</th><th>Verified</th><th>Status</th><th>Created</th><th>Actions</th></tr></thead><tbody>
      ${users.map(user => `<tr>
        <td>${escapeHtml(user.email)}</td>
        <td><span class="status ${user.emailVerified ? 'success' : 'error'}">${user.emailVerified ? 'Verified' : 'Pending'}</span></td>
        <td><span class="status ${user.isActive ? 'success' : 'error'}">${user.isActive ? 'Active' : 'Inactive'}</span></td>
        <td>${formatDate(user.createdUtc)}</td>
        <td class="row-actions">
          <button class="button tonal" data-action="toggle-user" data-user-id="${user.id}" data-active="${!user.isActive}">${user.isActive ? 'Deactivate' : 'Activate'}</button>
          <button class="button outlined" data-action="assign-role" data-user-id="${user.id}" data-email="${escapeAttribute(user.email)}">Assign role</button>
          <button class="button text" data-action="remove-role" data-user-id="${user.id}" data-email="${escapeAttribute(user.email)}">Remove role</button>
        </td></tr>`).join('')}
    </tbody></table></div>
    <script type="application/json" id="roles-data">${safeJson(roles)}</script>`;
}

async function renderRoles() {
  const [roles, permissions] = await Promise.all([page('/admin/roles?limit=200'), page('/admin/permissions?limit=200')]);
  elements.content.innerHTML = `
    ${pageHeader('Roles', 'Create global roles and change their permission grants.', '<button class="button primary" data-action="create-role">Create role</button>')}
    <div class="table-wrap"><table><thead><tr><th>Name</th><th>Description</th><th>Type</th><th>Permission actions</th></tr></thead><tbody>
      ${roles.map(role => `<tr><td>${escapeHtml(role.name)}</td><td>${escapeHtml(role.description)}</td><td><span class="status">${role.isSystem ? 'System' : 'Dynamic'}</span></td><td class="row-actions">
        <button class="button tonal" data-action="grant-permission" data-role-id="${role.id}" data-role-name="${escapeAttribute(role.name)}">Grant</button>
        <button class="button text" data-action="revoke-permission" data-role-id="${role.id}" data-role-name="${escapeAttribute(role.name)}">Revoke</button>
      </td></tr>`).join('')}
    </tbody></table></div>
    <script type="application/json" id="permissions-data">${safeJson(permissions)}</script>`;
}

async function renderPermissions() {
  const permissions = await page('/admin/permissions?limit=200');
  elements.content.innerHTML = `
    ${pageHeader('Permissions', 'The provider-backed global permission catalog exposed by SharpAccess.')}
    <div class="table-wrap"><table><thead><tr><th>Name</th><th>Description</th><th>ID</th></tr></thead><tbody>
      ${permissions.map(permission => `<tr><td><code>${escapeHtml(permission.name)}</code></td><td>${escapeHtml(permission.description)}</td><td><code>${permission.id}</code></td></tr>`).join('')}
    </tbody></table></div>`;
}

async function renderTenants() {
  const tenants = await page('/tenants?limit=200');
  elements.content.innerHTML = `
    ${pageHeader('Tenants', 'Create tenants, activate a tenant authorization context, and manage members.', '<button class="button primary" data-action="create-tenant">Create tenant</button>')}
    <section class="grid cards">
      ${tenants.map(tenant => `<article class="card"><p class="eyebrow">${escapeHtml(tenant.slug)}</p><h2>${escapeHtml(tenant.name)}</h2><p>${tenant.id}</p><div class="row-actions">
        <button class="button primary" data-action="activate-tenant" data-tenant-id="${tenant.id}" data-tenant-name="${escapeAttribute(tenant.name)}">Activate</button>
        <button class="button tonal" data-action="tenant-members" data-tenant-id="${tenant.id}" data-tenant-name="${escapeAttribute(tenant.name)}">Members</button>
      </div></article>`).join('') || emptyMarkup('No tenants are available.')}
    </section>`;
}

async function renderTenantMembers(tenantId, tenantName) {
  if (state.activeTenant !== tenantId) {
    showSnackbar('Activate this tenant before reading its members.');
    return activateTenant(tenantId, tenantName, true);
  }
  const [members, users] = await Promise.all([
    page(`/tenants/${tenantId}/members?limit=200`),
    optionalPage('/admin/users?limit=200')
  ]);
  elements.content.innerHTML = `
    ${pageHeader(`${tenantName} members`, 'Membership and tenant-role testing use the active tenant token.', `<button class="button primary" data-action="add-member" data-tenant-id="${tenantId}">Add member</button>`)}
    <div class="table-wrap"><table><thead><tr><th>Email</th><th>Owner</th><th>Tenant roles</th><th>Actions</th></tr></thead><tbody>
      ${members.map(member => `<tr><td>${escapeHtml(member.email)}</td><td>${member.isOwner ? 'Yes' : 'No'}</td><td>${(member.roles || []).map(role => `<span class="chip">${escapeHtml(role)}</span>`).join(' ')}</td><td><button class="button tonal" data-action="assign-tenant-role" data-tenant-id="${tenantId}" data-user-id="${member.userId}" data-email="${escapeAttribute(member.email)}">Assign role ID</button></td></tr>`).join('')}
    </tbody></table></div>
    <script type="application/json" id="users-data">${safeJson(users || [])}</script>`;
}

async function renderModules() {
  const [modules, users, roles] = await Promise.all([
    request('/sample/modules'),
    optionalPage('/admin/users?limit=200'),
    optionalPage('/admin/roles?limit=200')
  ]);
  elements.content.innerHTML = `
    ${pageHeader('Modules', 'Sample-only POCO modules map to real SharpAccess permissions through dedicated global roles.')}
    <section class="grid cards">
      ${modules.items.map(module => {
        const role = roles?.find(item => item.name === module.roleName);
        return `<article class="card module-card"><div class="module-icon">${escapeHtml(module.icon.slice(0, 1).toUpperCase())}</div><div><h2>${escapeHtml(module.displayName)}</h2><p>${escapeHtml(module.description)}</p></div><div class="permission-list"><span class="chip">${escapeHtml(module.permissionName)}</span><span class="status ${module.granted ? 'success' : ''}">${module.granted ? 'Granted to you' : 'Not granted to you'}</span></div><button class="button tonal" data-action="assign-module" data-role-id="${role?.id || ''}" data-module-name="${escapeAttribute(module.displayName)}" ${!role || !users ? 'disabled' : ''}>Assign to user</button></article>`;
      }).join('')}
    </section>
    <script type="application/json" id="users-data">${safeJson(users || [])}</script>`;
}

async function renderAudit() {
  const records = await page('/admin/audit-logs?limit=200');
  elements.content.innerHTML = `
    ${pageHeader('Audit', 'Bounded security events generated by sample setup and console mutations.')}
    <div class="table-wrap"><table><thead><tr><th>Time</th><th>Event</th><th>User</th><th>Tenant</th><th>Detail</th></tr></thead><tbody>
      ${records.map(record => `<tr><td>${formatDate(record.createdUtc)}</td><td><code>${escapeHtml(record.eventType)}</code></td><td>${record.userId || '—'}</td><td>${record.tenantId || '—'}</td><td>${escapeHtml(record.detail || '')}</td></tr>`).join('')}
    </tbody></table></div>`;
}

async function renderSettings() {
  const status = await request('/sample/status');
  elements.content.innerHTML = `
    ${pageHeader('Sample settings', 'Only safe metadata is available in the browser. Secret values never leave Windows Credential Manager.')}
    <section class="grid cards">
      <article class="card"><h2>Configuration storage</h2><p>${escapeHtml(status.setupStorage)}</p><span class="status success">Configured</span></article>
      <article class="card"><h2>Frontend</h2><p>${escapeHtml(status.frontend.design)}</p><span class="status">Framework: ${escapeHtml(status.frontend.framework)}</span></article>
      <article class="card"><h2>External providers</h2><p>${status.providers.length ? status.providers.map(escapeHtml).join(', ') : 'None enabled'}</p></article>
    </section>
    <section class="card" style="margin-top:18px"><h2>Reset commands</h2><p>Run these from PowerShell 7 after stopping the sample:</p><pre class="code-block">${escapeHtml(status.resetSetupCommand)}\n${escapeHtml(status.resetDataCommand)}</pre></section>`;
}

async function handleAction(action, target) {
  switch (action) {
    case 'refresh': return renderRoute(location.pathname);
    case 'show-register': return registerUser();
    case 'show-forgot': return forgotPassword();
    case 'toggle-user': return toggleUser(target.dataset.userId, target.dataset.active === 'true');
    case 'assign-role': return changeUserRole(target, true);
    case 'remove-role': return changeUserRole(target, false);
    case 'create-role': return createRole();
    case 'grant-permission': return changeRolePermission(target, true);
    case 'revoke-permission': return changeRolePermission(target, false);
    case 'create-tenant': return createTenant();
    case 'activate-tenant': return activateTenant(target.dataset.tenantId, target.dataset.tenantName);
    case 'tenant-members': return renderTenantMembers(target.dataset.tenantId, target.dataset.tenantName);
    case 'add-member': return addTenantMember(target.dataset.tenantId);
    case 'assign-tenant-role': return assignTenantRole(target);
    case 'assign-module': return assignModule(target);
  }
}

async function registerUser() {
  const values = await openDialog('Register test user', [
    { name: 'email', label: 'Email', type: 'email', required: true },
    { name: 'password', label: 'Password', type: 'password', required: true }
  ], 'Register');
  if (!values) return;
  await request('/auth/register', { method: 'POST', body: values });
  showSnackbar('Registration accepted. Use the verification link printed by the local sample mailbox.');
}

async function forgotPassword() {
  const values = await openDialog('Request password reset', [
    { name: 'email', label: 'Email', type: 'email', required: true }
  ], 'Send');
  if (!values) return;
  await request('/auth/forgot-password', { method: 'POST', body: values });
  showSnackbar('If the account exists, the local sample mailbox printed a reset link.');
}

async function toggleUser(userId, isActive) {
  await request(`/admin/users/${userId}/status`, { method: 'PATCH', body: { isActive } });
  showSnackbar(`User ${isActive ? 'activated' : 'deactivated'}.`);
  await renderUsers();
}

async function changeUserRole(target, assign) {
  const roles = readEmbedded('roles-data');
  const values = await openDialog(`${assign ? 'Assign' : 'Remove'} role for ${target.dataset.email}`, [
    { name: 'roleId', label: 'Global role', type: 'select', options: roles.map(role => ({ value: role.id, label: role.name })) }
  ], assign ? 'Assign' : 'Remove');
  if (!values) return;
  const path = `/admin/users/${target.dataset.userId}/roles${assign ? '' : `/${values.roleId}`}`;
  await request(path, assign ? { method: 'POST', body: { roleId: values.roleId } } : { method: 'DELETE' });
  showSnackbar(`Role ${assign ? 'assigned' : 'removed'}.`);
}

async function createRole() {
  const values = await openDialog('Create global role', [
    { name: 'name', label: 'Name', required: true },
    { name: 'description', label: 'Description', type: 'textarea', required: true }
  ], 'Create');
  if (!values) return;
  await request('/admin/roles', { method: 'POST', body: values });
  showSnackbar('Role created.');
  await renderRoles();
}

async function changeRolePermission(target, grant) {
  const permissions = readEmbedded('permissions-data');
  const values = await openDialog(`${grant ? 'Grant' : 'Revoke'} permission for ${target.dataset.roleName}`, [
    { name: 'permissionId', label: 'Permission', type: 'select', options: permissions.map(permission => ({ value: permission.id, label: permission.name })) }
  ], grant ? 'Grant' : 'Revoke');
  if (!values) return;
  const path = `/admin/roles/${target.dataset.roleId}/permissions${grant ? '' : `/${values.permissionId}`}`;
  await request(path, grant ? { method: 'POST', body: { permissionId: values.permissionId } } : { method: 'DELETE' });
  showSnackbar(`Permission ${grant ? 'granted' : 'revoked'}.`);
}

async function createTenant() {
  const values = await openDialog('Create tenant', [
    { name: 'name', label: 'Name', required: true },
    { name: 'slug', label: 'Slug', required: true }
  ], 'Create');
  if (!values) return;
  await request('/tenants', { method: 'POST', body: values });
  showSnackbar('Tenant created.');
  await renderTenants();
}

async function activateTenant(tenantId, tenantName, showMembers = false) {
  const values = await openDialog(`Activate ${tenantName}`, [
    { name: 'password', label: `Password for ${state.me.email}`, type: 'password', required: true }
  ], 'Activate');
  if (!values) return;
  const session = await request('/auth/login', {
    method: 'POST',
    body: { email: state.me.email, password: values.password, tenantId }
  });
  state.token = session.accessToken;
  await loadIdentity();
  showSnackbar(`Active tenant changed to ${tenantName}.`);
  if (showMembers) await renderTenantMembers(tenantId, tenantName);
  else await renderTenants();
}

async function addTenantMember(tenantId) {
  const users = readEmbedded('users-data');
  const values = await openDialog('Add tenant member', [
    { name: 'userId', label: 'User', type: 'select', options: users.map(user => ({ value: user.id, label: user.email })) }
  ], 'Add');
  if (!values) return;
  await request(`/tenants/${tenantId}/members`, { method: 'POST', body: { userId: values.userId } });
  showSnackbar('Tenant member added.');
}

async function assignTenantRole(target) {
  const values = await openDialog(`Assign tenant role to ${target.dataset.email}`, [
    { name: 'roleId', label: 'Tenant role ID', required: true, placeholder: 'GUID from provider/test fixture' }
  ], 'Assign');
  if (!values) return;
  await request(`/tenants/${target.dataset.tenantId}/members/${target.dataset.userId}/roles`, {
    method: 'POST', body: { roleId: values.roleId }
  });
  showSnackbar('Tenant role assigned.');
}

async function assignModule(target) {
  if (!target.dataset.roleId) return;
  const users = readEmbedded('users-data');
  const values = await openDialog(`Assign ${target.dataset.moduleName}`, [
    { name: 'userId', label: 'User', type: 'select', options: users.map(user => ({ value: user.id, label: user.email })) }
  ], 'Assign');
  if (!values) return;
  await request(`/admin/users/${values.userId}/roles`, {
    method: 'POST', body: { roleId: target.dataset.roleId }
  });
  showSnackbar('Module role assigned. The user must sign in again to receive updated claims.');
}

async function openDialog(title, fields, submitText) {
  elements.dialogTitle.textContent = title;
  elements.dialogSubmit.textContent = submitText;
  elements.dialogBody.innerHTML = fields.map(field => {
    const required = field.required === false ? '' : 'required';
    const placeholder = field.placeholder ? `placeholder="${escapeAttribute(field.placeholder)}"` : '';
    if (field.type === 'select') {
      return `<label class="field"><span>${escapeHtml(field.label)}</span><select name="${field.name}" ${required}>${field.options.map(option => `<option value="${escapeAttribute(option.value)}">${escapeHtml(option.label)}</option>`).join('')}</select></label>`;
    }
    if (field.type === 'textarea') {
      return `<label class="field"><span>${escapeHtml(field.label)}</span><textarea name="${field.name}" rows="4" ${required} ${placeholder}></textarea></label>`;
    }
    return `<label class="field"><span>${escapeHtml(field.label)}</span><input name="${field.name}" type="${field.type || 'text'}" ${required} ${placeholder}></label>`;
  }).join('');
  elements.dialog.showModal();
  return new Promise(resolve => {
    const onClose = () => {
      elements.dialog.removeEventListener('close', onClose);
      if (elements.dialog.returnValue !== 'default') return resolve(null);
      const data = Object.fromEntries(new FormData(elements.dialog.querySelector('form')).entries());
      resolve(data);
    };
    elements.dialog.addEventListener('close', onClose);
  });
}

function renderAccountHints() {
  const accounts = state.status?.accounts || {};
  elements.accounts.innerHTML = Object.entries(accounts)
    .map(([role, email]) => `<p><strong>${escapeHtml(role)}:</strong> ${escapeHtml(email)}</p>`).join('');
}

function renderOidcOptions() {
  const providers = state.status?.providers || [];
  elements.oidc.innerHTML = providers.map(provider =>
    `<button class="button outlined" data-action="oidc" data-provider="${escapeAttribute(provider)}" type="button">Continue with ${escapeHtml(provider)}</button>`).join('');
  elements.oidc.querySelectorAll('[data-action="oidc"]').forEach(button => button.addEventListener('click', async () => {
    try {
      const result = await request(`/auth/oauth/${encodeURIComponent(button.dataset.provider)}/challenge?returnUrl=${encodeURIComponent(location.origin + '/')}`);
      const target = result.authorizationUrl || result.url || result.redirectUrl;
      if (!target) throw new Error('The provider challenge did not return a redirect URL.');
      location.assign(target);
    } catch (error) { elements.loginError.textContent = describe(error); }
  }));
}

async function page(path) {
  const payload = await request(path);
  return payload.items || [];
}
async function optionalPage(path) {
  try { return await page(path); } catch (error) { if (error instanceof HttpError && [401, 403].includes(error.status)) return null; throw error; }
}
async function optionalRequest(path) {
  try { return await request(path); } catch (error) { if (error instanceof HttpError && [401, 403].includes(error.status)) return null; throw error; }
}

function pageHeader(title, subtitle, actions = '') {
  return `<header class="page-header"><div><h1>${escapeHtml(title)}</h1><p>${escapeHtml(subtitle)}</p></div><div class="page-actions">${actions}</div></header>`;
}
function metricCard(value, label) { return `<article class="card"><div class="metric">${value}</div><div class="label">${escapeHtml(label)}</div></article>`; }
function loadingMarkup() { return '<section class="empty-state"><div><div class="brand-mark small">S</div><h2>Loading…</h2></div></section>'; }
function emptyMarkup(message) { return `<div class="empty-state"><p>${escapeHtml(message)}</p></div>`; }

function renderError(status, title, detail, traceId = null) {
  elements.content.innerHTML = `<section class="error-state"><div><div class="code">${Number(status) || 500}</div><h1>${escapeHtml(title || 'Request failed')}</h1><p>${escapeHtml(detail || 'The request could not be completed.')}</p>${traceId ? `<p><code>Trace: ${escapeHtml(traceId)}</code></p>` : ''}<button class="button primary" data-action="refresh">Try again</button></div></section>`;
}

function showUnhandled(error) {
  if (error instanceof HttpError) renderError(error.status, error.title, error.detail, error.traceId);
  else renderError(500, 'Unexpected error', error?.message || 'An unexpected error occurred.');
}
function describe(error) { return error instanceof HttpError ? `${error.title}: ${error.detail}` : error?.message || 'Request failed.'; }
function formatDate(value) { return value ? new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '—'; }
function readEmbedded(id) { const element = document.getElementById(id); return element ? JSON.parse(element.textContent) : []; }
function safeJson(value) { return JSON.stringify(value).replaceAll('<', '\\u003c'); }
function escapeHtml(value) { return String(value ?? '').replace(/[&<>"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[character]); }
function escapeAttribute(value) { return escapeHtml(value).replaceAll("'", '&#39;'); }

function showSnackbar(message) {
  clearTimeout(state.snackbarTimer);
  elements.snackbar.textContent = message;
  elements.snackbar.classList.add('show');
  state.snackbarTimer = setTimeout(() => elements.snackbar.classList.remove('show'), 4200);
}

function applyStoredTheme() {
  const theme = localStorage.getItem('sharpaccess-sample-theme');
  if (theme) document.documentElement.dataset.theme = theme;
}
function toggleTheme() {
  const theme = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
  document.documentElement.dataset.theme = theme;
  localStorage.setItem('sharpaccess-sample-theme', theme);
}

boot();
