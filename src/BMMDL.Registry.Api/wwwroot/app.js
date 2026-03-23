/**
 * BMMDL Platform - Frontend Application
 * Clean rewrite with proper authentication handling
 */

// ===== State Management =====
const state = {
    user: null,
    token: null,
    currentModule: 'overview'
};

// ===== API Configuration =====
const API = {
    base: '/api',

    async request(endpoint, options = {}) {
        const url = `${this.base}${endpoint}`;
        const headers = {
            'Content-Type': 'application/json',
            ...options.headers
        };

        if (state.token) {
            headers['Authorization'] = `Bearer ${state.token}`;
        }

        try {
            const response = await fetch(url, { ...options, headers });
            const data = await response.json().catch(() => null);

            if (!response.ok) {
                throw new Error(data?.message || data?.title || `Request failed: ${response.status}`);
            }

            return data;
        } catch (error) {
            console.error(`API Error [${endpoint}]:`, error);
            throw error;
        }
    },

    // Auth endpoints
    async login(email, password) {
        return this.request('/auth/login', {
            method: 'POST',
            body: JSON.stringify({ email, password })
        });
    },

    async signup(name, email, password) {
        return this.request('/auth/signup', {
            method: 'POST',
            body: JSON.stringify({ name, email, password })
        });
    },

    // Tenants - use registry endpoint (no auth required for read)
    async getTenants() {
        return this.request('/registry/tenants');
    },

    async createTenant(data) {
        return this.request('/registry/tenants', {
            method: 'POST',
            body: JSON.stringify(data)
        });
    },

    // Configurations
    async getConfigurations() {
        return this.request('/configurations');
    },

    // Modules - use registry endpoint (no auth required)
    async getModules() {
        return this.request('/registry/modules');
    }
};

// ===== UI Helpers =====
function $(selector) {
    return document.querySelector(selector);
}

function $$(selector) {
    return document.querySelectorAll(selector);
}

function showToast(message, type = 'success') {
    const toast = $('#toast');
    toast.textContent = message;
    toast.className = `toast ${type} show`;

    setTimeout(() => {
        toast.classList.remove('show');
    }, 3000);
}

function setLoading(element, loading = true) {
    if (loading) {
        element.disabled = true;
        element.dataset.originalText = element.textContent;
        element.innerHTML = '<span class="spinner" style="width:20px;height:20px;border-width:2px;"></span>';
    } else {
        element.disabled = false;
        element.textContent = element.dataset.originalText || 'Submit';
    }
}

// ===== Auth Functions =====
function showAuthSection() {
    $('#auth-section').classList.remove('hidden');
    $('#dashboard-section').classList.add('hidden');
}

function showDashboard() {
    $('#auth-section').classList.add('hidden');
    $('#dashboard-section').classList.remove('hidden');

    // Update user info
    if (state.user) {
        $('#user-name').textContent = state.user.name || 'User';
        $('#user-email').textContent = state.user.email || '';
    }

    // Load initial module
    loadModule(state.currentModule);
}

function showLoginForm() {
    $('#login-form').classList.add('active');
    $('#signup-form').classList.remove('active');
    $('#login-error').textContent = '';
}

function showSignupForm() {
    $('#signup-form').classList.add('active');
    $('#login-form').classList.remove('active');
    $('#signup-error').textContent = '';
}

async function handleLogin(e) {
    e.preventDefault();

    const email = $('#login-email').value.trim();
    const password = $('#login-password').value;
    const submitBtn = e.target.querySelector('button[type="submit"]');
    const errorDiv = $('#login-error');

    errorDiv.textContent = '';
    setLoading(submitBtn, true);

    try {
        const result = await API.login(email, password);

        // Store auth data
        state.token = result.token;
        state.user = {
            id: result.userId,
            email: result.email,
            name: result.name || email.split('@')[0]
        };

        // Save to localStorage
        localStorage.setItem('bmmdl_token', state.token);
        localStorage.setItem('bmmdl_user', JSON.stringify(state.user));

        showToast('Welcome back!', 'success');
        showDashboard();

    } catch (error) {
        errorDiv.textContent = error.message || 'Login failed. Please try again.';
    } finally {
        setLoading(submitBtn, false);
    }
}

async function handleSignup(e) {
    e.preventDefault();

    const name = $('#signup-name').value.trim();
    const email = $('#signup-email').value.trim();
    const password = $('#signup-password').value;
    const confirm = $('#signup-confirm').value;
    const submitBtn = e.target.querySelector('button[type="submit"]');
    const errorDiv = $('#signup-error');

    errorDiv.textContent = '';

    // Validation
    if (password !== confirm) {
        errorDiv.textContent = 'Passwords do not match.';
        return;
    }

    if (password.length < 6) {
        errorDiv.textContent = 'Password must be at least 6 characters.';
        return;
    }

    setLoading(submitBtn, true);

    try {
        const result = await API.signup(name, email, password);

        // Store auth data
        state.token = result.token;
        state.user = {
            id: result.userId,
            email: result.email,
            name: name
        };

        // Save to localStorage
        localStorage.setItem('bmmdl_token', state.token);
        localStorage.setItem('bmmdl_user', JSON.stringify(state.user));

        showToast('Account created successfully!', 'success');
        showDashboard();

    } catch (error) {
        errorDiv.textContent = error.message || 'Signup failed. Please try again.';
    } finally {
        setLoading(submitBtn, false);
    }
}

function handleLogout() {
    state.token = null;
    state.user = null;
    localStorage.removeItem('bmmdl_token');
    localStorage.removeItem('bmmdl_user');

    // Clear form inputs
    $('#login-email').value = '';
    $('#login-password').value = '';
    $('#signup-name').value = '';
    $('#signup-email').value = '';
    $('#signup-password').value = '';
    $('#signup-confirm').value = '';

    showLoginForm();
    showAuthSection();
    showToast('Signed out successfully', 'success');
}

// ===== Module Loading =====
function loadModule(moduleName) {
    state.currentModule = moduleName;

    // Update nav state
    $$('.nav-item').forEach(item => {
        item.classList.toggle('active', item.dataset.module === moduleName);
    });

    // Update header
    const titles = {
        overview: 'Overview',
        tenants: 'Tenant Management',
        modules: 'Modules',
        settings: 'Settings'
    };
    $('#module-title').textContent = titles[moduleName] || 'Dashboard';

    // Load content
    const content = $('#module-content');
    content.innerHTML = '<div class="loading"><div class="spinner"></div></div>';

    switch (moduleName) {
        case 'overview':
            loadOverview();
            break;
        case 'tenants':
            loadTenants();
            break;
        case 'modules':
            loadModules();
            break;
        case 'settings':
            loadSettings();
            break;
        default:
            content.innerHTML = '<div class="empty-state"><div class="icon">🚧</div><p>Module not found</p></div>';
    }
}

async function loadOverview() {
    const content = $('#module-content');

    try {
        // Load stats in parallel
        const [tenants, modules] = await Promise.allSettled([
            API.getTenants(),
            API.getModules()
        ]);

        const tenantCount = tenants.status === 'fulfilled' ? (tenants.value?.length || 0) : 0;
        const moduleCount = modules.status === 'fulfilled' ? (modules.value?.length || 0) : 0;

        content.innerHTML = `
            <div class="stats-grid">
                <div class="stat-card">
                    <div class="stat-icon">🏢</div>
                    <div class="stat-value">${tenantCount}</div>
                    <div class="stat-label">Active Tenants</div>
                </div>
                <div class="stat-card">
                    <div class="stat-icon">📦</div>
                    <div class="stat-value">${moduleCount}</div>
                    <div class="stat-label">Modules</div>
                </div>
                <div class="stat-card">
                    <div class="stat-icon">✅</div>
                    <div class="stat-value">Healthy</div>
                    <div class="stat-label">System Status</div>
                </div>
            </div>
            
            <div class="card">
                <div class="card-header">
                    <h3 class="card-title">Welcome to BMMDL Platform</h3>
                </div>
                <p style="color: var(--text-secondary);">
                    Manage your business meta models, tenants, and configurations from this dashboard.
                </p>
            </div>
        `;
    } catch (error) {
        content.innerHTML = `
            <div class="empty-state">
                <div class="icon">⚠️</div>
                <p>Failed to load overview: ${error.message}</p>
            </div>
        `;
    }
}

async function loadTenants() {
    const content = $('#module-content');

    try {
        const tenants = await API.getTenants();

        if (!tenants || tenants.length === 0) {
            content.innerHTML = `
                <div class="empty-state">
                    <div class="icon">🏢</div>
                    <p>No tenants found</p>
                    <button class="btn btn-primary btn-sm" style="margin-top: 1rem;" onclick="showCreateTenantForm()">
                        Create First Tenant
                    </button>
                </div>
            `;
            return;
        }

        content.innerHTML = `
            <div class="card">
                <div class="card-header">
                    <h3 class="card-title">All Tenants</h3>
                    <button class="btn btn-primary btn-sm" onclick="showCreateTenantForm()">+ Add Tenant</button>
                </div>
                <table class="data-table">
                    <thead>
                        <tr>
                            <th>Name</th>
                            <th>Identifier</th>
                            <th>Status</th>
                            <th>Created</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${tenants.map(t => `
                            <tr>
                                <td>${escapeHtml(t.name)}</td>
                                <td><code>${escapeHtml(t.identifier)}</code></td>
                                <td><span class="badge badge-${t.isActive ? 'success' : 'warning'}">${t.isActive ? 'Active' : 'Inactive'}</span></td>
                                <td>${formatDate(t.createdAt)}</td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            </div>
        `;
    } catch (error) {
        content.innerHTML = `
            <div class="empty-state">
                <div class="icon">⚠️</div>
                <p>Failed to load tenants: ${error.message}</p>
            </div>
        `;
    }
}

async function loadModules() {
    const content = $('#module-content');

    try {
        const modules = await API.getModules();

        if (!modules || modules.length === 0) {
            content.innerHTML = `
                <div class="empty-state">
                    <div class="icon">📦</div>
                    <p>No modules registered</p>
                </div>
            `;
            return;
        }

        content.innerHTML = `
            <div class="card">
                <div class="card-header">
                    <h3 class="card-title">Registered Modules</h3>
                </div>
                <table class="data-table">
                    <thead>
                        <tr>
                            <th>Name</th>
                            <th>Version</th>
                            <th>Entities</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${modules.map(m => `
                            <tr>
                                <td>${escapeHtml(m.name)}</td>
                                <td>${escapeHtml(m.version || '1.0')}</td>
                                <td>${m.entityCount || 0}</td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            </div>
        `;
    } catch (error) {
        content.innerHTML = `
            <div class="empty-state">
                <div class="icon">⚠️</div>
                <p>Failed to load modules: ${error.message}</p>
            </div>
        `;
    }
}

async function loadSettings() {
    const content = $('#module-content');

    try {
        const configs = await API.getConfigurations();
        const config = Array.isArray(configs) && configs.length > 0 ? configs[0] : configs;

        content.innerHTML = `
            <div class="card">
                <div class="card-header">
                    <h3 class="card-title">System Configuration</h3>
                </div>
                ${config ? `
                    <p><strong>Environment:</strong> ${escapeHtml(config.environment || 'Development')}</p>
                    <p><strong>Database Endpoints:</strong> ${config.databaseEndpoints?.length || 0}</p>
                    <p><strong>Status:</strong> ${escapeHtml(config.status || 'Unknown')}</p>
                ` : '<p style="color: var(--text-secondary);">No configuration found. Create one from the backend.</p>'}
            </div>
            
            <div class="card" style="margin-top: 1rem;">
                <div class="card-header">
                    <h3 class="card-title">User Settings</h3>
                </div>
                <p><strong>User:</strong> ${escapeHtml(state.user?.name || 'Unknown')}</p>
                <p><strong>Email:</strong> ${escapeHtml(state.user?.email || 'Unknown')}</p>
            </div>
        `;
    } catch (error) {
        // Show user info even if config fails
        content.innerHTML = `
            <div class="card">
                <div class="card-header">
                    <h3 class="card-title">Settings</h3>
                </div>
                <p style="color: var(--text-secondary);">System configuration requires admin access.</p>
            </div>
            
            <div class="card" style="margin-top: 1rem;">
                <div class="card-header">
                    <h3 class="card-title">User Settings</h3>
                </div>
                <p><strong>User:</strong> ${escapeHtml(state.user?.name || 'Unknown')}</p>
                <p><strong>Email:</strong> ${escapeHtml(state.user?.email || 'Unknown')}</p>
            </div>
        `;
    }
}

// ===== Utility Functions =====
function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function formatDate(dateStr) {
    if (!dateStr) return '-';
    try {
        return new Date(dateStr).toLocaleDateString();
    } catch {
        return dateStr;
    }
}

// Placeholder for create tenant form
function showCreateTenantForm() {
    showToast('Create tenant form - coming soon', 'success');
}

// ===== Initialization =====
function init() {
    // Check for existing session
    const savedToken = localStorage.getItem('bmmdl_token');
    const savedUser = localStorage.getItem('bmmdl_user');

    if (savedToken && savedUser) {
        try {
            state.token = savedToken;
            state.user = JSON.parse(savedUser);
            showDashboard();
        } catch {
            showAuthSection();
        }
    } else {
        showAuthSection();
    }

    // Event Listeners
    $('#login-form').addEventListener('submit', handleLogin);
    $('#signup-form').addEventListener('submit', handleSignup);
    $('#show-signup').addEventListener('click', (e) => { e.preventDefault(); showSignupForm(); });
    $('#show-login').addEventListener('click', (e) => { e.preventDefault(); showLoginForm(); });
    $('#logout-btn').addEventListener('click', handleLogout);

    // Navigation
    $$('.nav-item').forEach(item => {
        item.addEventListener('click', () => {
            loadModule(item.dataset.module);
        });
    });
}

// Start the app
document.addEventListener('DOMContentLoaded', init);
