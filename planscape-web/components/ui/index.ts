/** U3 — one import path for the primitives: `@/components/ui`. */
export { Button, type ButtonProps } from './Button';
export {
  Badge,
  Card,
  EmptyState,
  ErrorNote,
  Input,
  LoadingBlock,
  PageHeader,
  Select,
  Skeleton,
  SkeletonRows,
  Toolbar,
  ToolbarSpacer,
  toneForStatus,
  type BadgeProps,
} from './primitives';
export { Modal, Drawer, Tabs, TabPanel } from './overlays';
export { DataGrid, type Column, type DataGridProps } from './DataGrid';
// Re-exported so a page building a DataGrid's `rowMenu` has one import path.
// The menu itself stays in components/shell — it is the shell's menu, and the
// point of U6 was to reuse it rather than grow a second one here.
export { MenuItem, MenuLabel, MenuSeparator } from '@/components/shell/Menu';
export { ToastProvider, useToast, type ToastTone } from './toast';
