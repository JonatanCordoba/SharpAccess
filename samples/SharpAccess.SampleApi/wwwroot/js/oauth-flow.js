(() => {
  const ErrorStorageKey = 'sharpaccess-sample-oauth-error';

  function toMessage(value) {
    if (typeof value === 'string') return value.trim();
    if (value === null || value === undefined) return '';
    if (Array.isArray(value)) {
      return value.map(toMessage).filter(Boolean).join(' ');
    }
    if (typeof value === 'object') {
      return Object.values(value).map(toMessage).filter(Boolean).join(' ');
    }
    return String(value);
  }

  async function readFailure(response) {
    const contentType = response.headers.get('content-type') || '';
    let payload = null;
    try {
      payload = contentType.includes('json') ? await response.json() : await response.text();
    } catch {
      payload = null;
    }

    if (payload && typeof payload === 'object') {
      const detail = toMessage(payload.detail)
        || toMessage(payload.errors)
        || toMessage(payload.message)
        || toMessage(payload.error_description)
        || toMessage(payload.error);
      const title = toMessage(payload.title);
      if (title && detail) return `${title}: ${detail}`;
      if (detail || title) return detail || title;
    }

    const text = toMessage(payload);
    return text || `External sign-in failed with HTTP ${response.status}.`;
  }

  function showStoredError() {
    const message = sessionStorage.getItem(ErrorStorageKey);
    if (!message) return;
    sessionStorage.removeItem(ErrorStorageKey);
    const display = () => {
      const error = document.querySelector('#login-error');
      if (error) error.textContent = message;
    };
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', display, { once: true });
    } else {
      display();
    }
  }

  function beginChallenge(provider) {
    const safeProvider = encodeURIComponent(provider);
    const localReturnUrl = `/?oauth_provider=${safeProvider}`;
    const challenge = `/auth/oauth/${safeProvider}/challenge?returnUrl=${encodeURIComponent(localReturnUrl)}`;
    location.assign(challenge);
  }

  async function completeCallback() {
    const query = new URLSearchParams(location.search);
    const fragment = new URLSearchParams(location.hash.startsWith('#') ? location.hash.slice(1) : location.hash);
    const provider = query.get('oauth_provider');
    const code = fragment.get('oauth_code');
    if (!provider && !code) return;

    history.replaceState({}, '', '/');
    if (!provider || !code) {
      sessionStorage.setItem(ErrorStorageKey, 'The external sign-in callback was incomplete. Please try again.');
      location.replace('/');
      return;
    }

    try {
      const response = await fetch(`/auth/oauth/${encodeURIComponent(provider)}/exchange`, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code, tenantId: null })
      });
      if (!response.ok) {
        throw new Error(await readFailure(response));
      }
      location.replace('/');
    } catch (error) {
      sessionStorage.setItem(
        ErrorStorageKey,
        error instanceof Error && error.message ? error.message : 'External sign-in could not be completed.');
      location.replace('/');
    }
  }

  document.addEventListener('click', event => {
    const button = event.target.closest('[data-action="oidc"][data-provider]');
    if (!button) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    const provider = button.dataset.provider;
    if (provider) beginChallenge(provider);
  }, true);

  showStoredError();
  void completeCallback();
})();
