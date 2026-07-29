import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import Components from 'unplugin-vue-components/vite'
import Icons from 'unplugin-icons/vite'
import IconsResolver from 'unplugin-icons/resolver'

export default defineConfig({
  plugins: [
    vue(),
    tailwindcss(),
    Components({
      resolvers: [IconsResolver()],
      dts: false,
    }),
    Icons({ compiler: 'vue3' }),
  ],
  resolve: {
    // The in-app logo is the same artwork the exe, installer and package icons
    // are generated from. Aliased out to the repo's icon/ directory rather than
    // copied into src/assets so the two can't drift.
    alias: {
      '@icon': fileURLToPath(new URL('../../../icon', import.meta.url)),
    },
  },
  base: './',
  server: {
    port: 5174,
    strictPort: true,
  },
})
