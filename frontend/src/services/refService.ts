import api from './api'

const enc = encodeURIComponent

export const refService = {
  /**
   * Create a reference (POST /{id}/{navProperty}/$ref)
   */
  async createRef(
    module: string,
    entitySet: string,
    id: string,
    navProperty: string,
    targetId: string,
    targetEntitySet: string
  ): Promise<void> {
    await api.post(`/odata/${enc(module)}/${enc(entitySet)}/${enc(id)}/${enc(navProperty)}/$ref`, {
      '@odata.id': `${enc(targetEntitySet)}/${enc(targetId)}`
    })
  },

  /**
   * Update a reference (PUT /{id}/{navProperty}/$ref)
   */
  async updateRef(
    module: string,
    entitySet: string,
    id: string,
    navProperty: string,
    targetId: string,
    targetEntitySet: string
  ): Promise<void> {
    await api.put(`/odata/${enc(module)}/${enc(entitySet)}/${enc(id)}/${enc(navProperty)}/$ref`, {
      '@odata.id': `${enc(targetEntitySet)}/${enc(targetId)}`
    })
  },

  /**
   * Delete a reference (DELETE /{id}/{navProperty}/$ref)
   */
  async deleteRef(
    module: string,
    entitySet: string,
    id: string,
    navProperty: string
  ): Promise<void> {
    await api.delete(`/odata/${enc(module)}/${enc(entitySet)}/${enc(id)}/${enc(navProperty)}/$ref`)
  },

  /**
   * Delete a M:M reference with target ID (DELETE /{id}/{navProperty}/$ref?$id=...)
   */
  async deleteRefWithTarget(
    module: string,
    entitySet: string,
    id: string,
    navProperty: string,
    targetId: string,
    targetEntitySet: string
  ): Promise<void> {
    await api.delete(`/odata/${enc(module)}/${enc(entitySet)}/${enc(id)}/${enc(navProperty)}/$ref`, {
      params: { '$id': `${enc(targetEntitySet)}/${enc(targetId)}` }
    })
  }
}

export default refService
