import { fileURLToPath, URL } from 'node:url'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [vue()],

  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },

  build: {
    // The build lands directly in the ASP.NET Core project's wwwroot. That is what makes this
    // one deployable: `dotnet run` then serves the API and these files from the same origin,
    // so there is no CORS to configure and no second host to keep in step.
    outDir: fileURLToPath(new URL('../Split.Api/wwwroot', import.meta.url)),
    emptyOutDir: true,
  },

  server: {
    // Only used by `npm run dev`. The dev server runs on its own port, so /api would be a
    // cross-origin request; proxying it to the backend makes development look exactly like
    // production from the browser's point of view.
    proxy: {
      '/api': 'http://127.0.0.1:5199',
    },
  },
})
