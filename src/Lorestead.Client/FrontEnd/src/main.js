import { createApp } from 'vue'
import { createPinia } from 'pinia'
import router from './router.js'
import App from './App.vue'
import { initPlatform } from './composables/usePlatform.js'

import './style.css'

if (import.meta.env.DEV && typeof window.galdrInvoke === 'undefined') {
  const { installMockBackend } = await import('./dev/mockBackend.js')
  installMockBackend()
}

await initPlatform()

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.mount('#app')
