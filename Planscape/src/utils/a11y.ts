import { AccessibilityProps } from 'react-native';

export function label(text: string, hint?: string): AccessibilityProps {
  return {
    accessible: true,
    accessibilityLabel: text,
    accessibilityHint: hint,
  };
}

export function button(text: string, hint?: string): AccessibilityProps {
  return {
    ...label(text, hint),
    accessibilityRole: 'button',
  };
}

export function heading(text: string): AccessibilityProps {
  return {
    ...label(text),
    accessibilityRole: 'header',
  };
}
