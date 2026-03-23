export interface FileReferenceInfo {
  provider?: string
  bucket?: string
  key?: string
  size?: number
  mimeType?: string
  checksum?: string
  uploadedAt?: string
  uploadedBy?: string
}

export interface UploadResult {
  provider: string
  bucket: string
  key: string
  size: number
  mimeType: string
  checksum: string
  uploadedAt: string
}
