import { ref, computed, type Ref, type ComputedRef } from 'vue'

export interface UseColorPickerOptions {
  initialColor?: string // hex string like '#ff0000'
}

export interface HSV {
  h: number // 0-360
  s: number // 0-100
  v: number // 0-100
}

export interface RGB {
  r: number // 0-255
  g: number // 0-255
  b: number // 0-255
}

export interface UseColorPickerReturn {
  hex: Ref<string>
  rgb: ComputedRef<RGB>
  hsv: Ref<HSV>
  setHex: (hex: string) => void
  setRGB: (r: number, g: number, b: number) => void
  setHSV: (h: number, s: number, v: number) => void
  // Conversion utilities
  hexToRgb: (hex: string) => RGB
  rgbToHex: (r: number, g: number, b: number) => string
  rgbToHsv: (r: number, g: number, b: number) => HSV
  hsvToRgb: (h: number, s: number, v: number) => RGB
}

// ── Conversion utilities ──────────────────────────────────────────────────

function hexToRgb(hex: string): RGB {
  const cleaned = hex.replace(/^#/, '')
  const full =
    cleaned.length === 3
      ? cleaned[0] + cleaned[0] + cleaned[1] + cleaned[1] + cleaned[2] + cleaned[2]
      : cleaned
  const num = parseInt(full, 16)
  return {
    r: (num >> 16) & 255,
    g: (num >> 8) & 255,
    b: num & 255,
  }
}

function rgbToHex(r: number, g: number, b: number): string {
  const clamp = (v: number) => Math.max(0, Math.min(255, Math.round(v)))
  const toHex = (v: number) => clamp(v).toString(16).padStart(2, '0')
  return '#' + toHex(r) + toHex(g) + toHex(b)
}

function rgbToHsv(r: number, g: number, b: number): HSV {
  const rn = r / 255
  const gn = g / 255
  const bn = b / 255
  const max = Math.max(rn, gn, bn)
  const min = Math.min(rn, gn, bn)
  const delta = max - min

  let h = 0
  if (delta !== 0) {
    if (max === rn) {
      h = ((gn - bn) / delta) % 6
    } else if (max === gn) {
      h = (bn - rn) / delta + 2
    } else {
      h = (rn - gn) / delta + 4
    }
    h = Math.round(h * 60)
    if (h < 0) h += 360
  }

  const s = max === 0 ? 0 : Math.round((delta / max) * 100)
  const v = Math.round(max * 100)

  return { h, s, v }
}

function hsvToRgb(h: number, s: number, v: number): RGB {
  const sn = s / 100
  const vn = v / 100
  const c = vn * sn
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1))
  const m = vn - c

  let rp = 0,
    gp = 0,
    bp = 0
  if (h < 60) {
    rp = c; gp = x; bp = 0
  } else if (h < 120) {
    rp = x; gp = c; bp = 0
  } else if (h < 180) {
    rp = 0; gp = c; bp = x
  } else if (h < 240) {
    rp = 0; gp = x; bp = c
  } else if (h < 300) {
    rp = x; gp = 0; bp = c
  } else {
    rp = c; gp = 0; bp = x
  }

  return {
    r: Math.round((rp + m) * 255),
    g: Math.round((gp + m) * 255),
    b: Math.round((bp + m) * 255),
  }
}

// ── Composable ────────────────────────────────────────────────────────────

export function useColorPicker(options: UseColorPickerOptions = {}): UseColorPickerReturn {
  const initial = options.initialColor ?? '#000000'
  const initialRgb = hexToRgb(initial)
  const initialHsv = rgbToHsv(initialRgb.r, initialRgb.g, initialRgb.b)

  const hex = ref(initial)
  const hsv = ref<HSV>({ ...initialHsv })

  const rgb = computed<RGB>(() => hsvToRgb(hsv.value.h, hsv.value.s, hsv.value.v))

  function setHex(value: string) {
    const cleaned = value.replace(/^#/, '')
    if (!/^[0-9a-fA-F]{3}([0-9a-fA-F]{3})?$/.test(cleaned)) return
    const normalized = cleaned.length === 3
      ? '#' + cleaned[0] + cleaned[0] + cleaned[1] + cleaned[1] + cleaned[2] + cleaned[2]
      : '#' + cleaned
    hex.value = normalized.toLowerCase()
    const { r, g, b } = hexToRgb(normalized)
    hsv.value = rgbToHsv(r, g, b)
  }

  function setRGB(r: number, g: number, b: number) {
    const cr = Math.max(0, Math.min(255, Math.round(r)))
    const cg = Math.max(0, Math.min(255, Math.round(g)))
    const cb = Math.max(0, Math.min(255, Math.round(b)))
    hex.value = rgbToHex(cr, cg, cb)
    hsv.value = rgbToHsv(cr, cg, cb)
  }

  function setHSV(h: number, s: number, v: number) {
    const ch = Math.max(0, Math.min(360, h))
    const cs = Math.max(0, Math.min(100, s))
    const cv = Math.max(0, Math.min(100, v))
    hsv.value = { h: ch, s: cs, v: cv }
    const { r, g, b } = hsvToRgb(ch, cs, cv)
    hex.value = rgbToHex(r, g, b)
  }

  return {
    hex,
    rgb,
    hsv,
    setHex,
    setRGB,
    setHSV,
    hexToRgb,
    rgbToHex,
    rgbToHsv,
    hsvToRgb,
  }
}
