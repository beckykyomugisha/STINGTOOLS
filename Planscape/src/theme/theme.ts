import { useColorScheme } from 'react-native';

export interface Theme {
  bg: string;
  surface: string;
  text: string;
  textMuted: string;
  accent: string;
  border: string;
}

const light: Theme = {
  bg: '#FFFFFF',
  surface: '#F5F7FA',
  text: '#1A1A1A',
  textMuted: '#6B7280',
  accent: '#1F3864',
  border: '#E5E7EB',
};

const dark: Theme = {
  bg: '#0F1115',
  surface: '#1A1D23',
  text: '#F3F4F6',
  textMuted: '#9CA3AF',
  accent: '#2E75B6',
  border: '#2A2E36',
};

export function useTheme(): Theme {
  const scheme = useColorScheme();
  return scheme === 'dark' ? dark : light;
}
