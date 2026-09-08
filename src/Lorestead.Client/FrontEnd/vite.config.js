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
  build: {
    rolldownOptions: {
      output: {
        // The editor and preview libraries are shared by the Notes and Boards
        // routes, so Rolldown puts them all in one chunk and names it after
        // whatever small component happens to be first in it - a 900 kB
        // "ConfirmDialog". Naming them keeps the build report honest about
        // where the weight is. Same bytes, same load order: the app serves
        // its assets from disk, so the chunk-size warning's web-latency
        // reasoning does not apply here anyway.
        codeSplitting: {
          // By default a group takes a matched module's dependencies with it,
          // which would fold everything CodeMirror into the first group listed.
          // Matching on path alone keeps each group to what its test names.
          includeDependenciesRecursively: false,
          groups: [
            // CodeMirror is cut at the one seam that cannot cycle: view and state
            // (plus their two helpers) import nothing else in CodeMirror, and
            // everything else imports them. Cutting anywhere else - the fenced-code
            // grammars, say - makes the two chunks import each other, and a class
            // is then used before its chunk has evaluated ("x is not a constructor"
            // at load).
            { name: 'codemirror-view', test: /node_modules\/(@codemirror\/(view|state)|style-mod|w3c-keyname)\// },
            { name: 'codemirror', test: /node_modules\/(@codemirror|@lezer|codemirror|crelt)\// },
            { name: 'highlight', test: /node_modules\/highlight\.js\// },
            { name: 'markdown', test: /node_modules\/(markdown-it|linkify-it|mdurl|uc\.micro|entities|punycode)/ },
          ],
        },
      },
    },
  },
  server: {
    port: 5174,
      strictPort: true,
      host: true
  },
})
