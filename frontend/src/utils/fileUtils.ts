export function formatFileSize(bytes: number): string {
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`
}

export function getFileIcon(mimeType: string): string {
  if (mimeType.startsWith('image/')) return 'Image'
  if (mimeType.startsWith('video/')) return 'Video'
  if (mimeType === 'application/pdf') return 'FileText'
  if (mimeType.includes('spreadsheet') || mimeType.includes('excel')) return 'Sheet'
  if (mimeType.includes('document') || mimeType.includes('word')) return 'FileText'
  return 'File'
}

export function isImageType(mimeType: string): boolean {
  return mimeType.startsWith('image/')
}

export function getFileNameFromKey(key: string): string {
  const fileName = key.split('/').pop() || key
  // Remove UUID prefix if present (UUID is 36 chars with hyphens)
  const underscoreIdx = fileName.indexOf('_')
  if (underscoreIdx > 30) {
    return fileName.substring(underscoreIdx + 1)
  }
  return fileName
}
