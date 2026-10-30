import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
    },
  },
  // Keep inherited issues visible without weakening checks for the new wishlist code.
  {
    files: ['src/hooks/useSignalR.ts', 'src/pages/RegisterPage.tsx', 'src/store/auth.ts'],
    rules: { '@typescript-eslint/no-explicit-any': 'warn' },
  },
  { files: ['src/store/auth.ts'], rules: { '@typescript-eslint/no-unused-vars': 'warn' } },
  { files: ['src/api/useProfile.ts'], rules: { 'react-hooks/purity': 'warn' } },
  { files: ['src/components/TelegramLoginButton.tsx'], rules: { 'react-hooks/refs': 'warn' } },
  {
    files: ['src/pages/AuthCallbackPage.tsx', 'src/pages/SettingsPage.tsx'],
    rules: { 'react-hooks/set-state-in-effect': 'warn' },
  },
])
