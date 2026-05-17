import type { Configuration } from 'electron-builder'

const config: Configuration = {
  appId: 'com.planscape.desktop',
  productName: 'Planscape Desktop',
  copyright: 'Copyright © 2026 Planscape Limited',
  asar: true,
  directories: {
    output: 'release',
    buildResources: 'resources'
  },
  files: [
    'dist-electron/**/*',
    'dist/renderer/**/*',
    '!**/*.map'
  ],
  extraResources: [
    {
      from: 'resources/',
      to: 'resources/',
      filter: ['**/*']
    }
  ],
  win: {
    target: [
      { target: 'nsis', arch: ['x64'] }
    ],
    icon: 'resources/icon.ico',
    artifactName: 'Planscape-Desktop-Setup-${version}.${ext}'
  },
  nsis: {
    oneClick: false,
    allowToChangeInstallationDirectory: true,
    createDesktopShortcut: true,
    createStartMenuShortcut: true,
    shortcutName: 'Planscape Desktop'
  },
  mac: {
    target: [
      { target: 'dmg', arch: ['x64', 'arm64'] }
    ],
    icon: 'resources/icon.icns',
    category: 'public.app-category.productivity',
    artifactName: 'Planscape-Desktop-${version}.${ext}'
  },
  linux: {
    target: ['AppImage', 'deb'],
    icon: 'resources/icon.png',
    category: 'Office',
    artifactName: 'Planscape-Desktop-${version}.${ext}'
  },
  publish: {
    provider: 'github',
    owner: 'planscape',
    repo: 'planscape-desktop'
  }
}

export default config
