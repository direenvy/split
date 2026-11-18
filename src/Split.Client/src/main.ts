import { createApp } from 'vue'

// Inter Variable, self-hosted. Bundling the font rather than linking to Google Fonts keeps
// this a single deployable with no third-party request on first paint — and means the app
// still looks right offline. The 'variable' import is the one that actually carries weight
// 200, which the design depends on.
import '@fontsource-variable/inter'

import '@/styles/theme.css'
import '@/styles/base.css'

import { createPinia } from 'pinia'
import App from '@/App.vue'
import { router } from '@/router'

// createApp returns an app instance you configure with .use(), rather than React's pattern of
// wrapping the tree in <Provider> and <BrowserRouter> components.
createApp(App).use(createPinia()).use(router).mount('#app')
