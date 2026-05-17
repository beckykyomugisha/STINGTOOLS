import React, { useState } from 'react'

export interface FolderNode {
  name: string
  path: string
  relativePath: string
  isDirectory: boolean
  children?: FolderNode[]
  fileCount?: number
}

const FILE_ICONS: Record<string, string> = {
  ifc: '🏗', rvt: '🏠', dwg: '📐', dxf: '📐',
  pdf: '📄', xlsx: '📊', xls: '📊', csv: '📊',
  docx: '📝', doc: '📝', png: '🖼', jpg: '🖼',
  dgn: '📐', zip: '📦', default: '📎'
}

function getIcon(name: string, isDir: boolean): string {
  if (isDir) return '📁'
  const ext = name.split('.').pop()?.toLowerCase() ?? 'default'
  return FILE_ICONS[ext] ?? FILE_ICONS.default
}

function TreeNode({
  node, depth = 0, onFileClick
}: {
  node: FolderNode
  depth?: number
  onFileClick?: (node: FolderNode) => void
}): React.ReactElement {
  const [expanded, setExpanded] = useState(depth < 2)

  const toggle = () => { if (node.isDirectory) setExpanded(e => !e) }
  const click = () => {
    if (!node.isDirectory) onFileClick?.(node)
  }

  return (
    <div>
      <div
        className={`flex items-center gap-1.5 py-0.5 px-2 rounded cursor-pointer select-none
          hover:bg-ps-elevated text-xs group`}
        style={{ paddingLeft: `${(depth * 16) + 8}px` }}
        onClick={node.isDirectory ? toggle : click}
      >
        {node.isDirectory && (
          <span className="text-ps-muted text-xs w-3">
            {expanded ? '▾' : '▸'}
          </span>
        )}
        {!node.isDirectory && <span className="w-3" />}
        <span className="shrink-0">{getIcon(node.name, node.isDirectory)}</span>
        <span className={`flex-1 truncate ${node.isDirectory ? 'text-ps-text font-medium' : 'text-ps-muted'}`}>
          {node.name}
        </span>
        {node.isDirectory && node.fileCount !== undefined && node.fileCount > 0 && (
          <span className="text-ps-muted text-xs opacity-0 group-hover:opacity-100 transition-opacity">
            {node.fileCount}
          </span>
        )}
      </div>
      {node.isDirectory && expanded && node.children && (
        <div>
          {node.children.map(child => (
            <TreeNode key={child.path} node={child} depth={depth + 1} onFileClick={onFileClick} />
          ))}
        </div>
      )}
    </div>
  )
}

interface FolderTreeProps {
  nodes: FolderNode[]
  onFileClick?: (node: FolderNode) => void
  className?: string
}

export default function FolderTree({ nodes, onFileClick, className = '' }: FolderTreeProps): React.ReactElement {
  if (nodes.length === 0) {
    return (
      <div className={`text-ps-muted text-xs p-4 text-center ${className}`}>
        No files found
      </div>
    )
  }
  return (
    <div className={`overflow-auto ${className}`}>
      {nodes.map(node => (
        <TreeNode key={node.path} node={node} depth={0} onFileClick={onFileClick} />
      ))}
    </div>
  )
}
