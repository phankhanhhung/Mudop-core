import api from './api'
import type { UploadResult } from '@/types/file'

const enc = encodeURIComponent

export const fileService = {
  async upload(
    module: string,
    entity: string,
    id: string,
    field: string,
    file: File
  ): Promise<UploadResult> {
    const formData = new FormData()
    formData.append('file', file)
    const response = await api.post<UploadResult>(
      `/files/${enc(module)}/${enc(entity)}/${enc(id)}/${enc(field)}`,
      formData,
      { headers: { 'Content-Type': 'multipart/form-data' } }
    )
    return response.data
  },

  async download(
    module: string,
    entity: string,
    id: string,
    field: string
  ): Promise<{ blob: Blob; contentType: string; fileName: string }> {
    const response = await api.get(`/files/${enc(module)}/${enc(entity)}/${enc(id)}/${enc(field)}`, {
      responseType: 'blob'
    })
    const contentType = response.headers['content-type'] || 'application/octet-stream'
    const disposition = response.headers['content-disposition'] || ''
    const fileNameMatch = disposition.match(/filename="?(.+?)"?(?:;|$)/)
    const fileName = fileNameMatch?.[1] || `${field}-${id}`
    return { blob: response.data, contentType, fileName }
  },

  async delete(module: string, entity: string, id: string, field: string): Promise<void> {
    await api.delete(`/files/${enc(module)}/${enc(entity)}/${enc(id)}/${enc(field)}`)
  },

  getDownloadUrl(module: string, entity: string, id: string, field: string): string {
    return `/api/files/${enc(module)}/${enc(entity)}/${enc(id)}/${enc(field)}`
  }
}

export default fileService
