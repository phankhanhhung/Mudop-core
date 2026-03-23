/**
 * One-time script to generate PWA icon PNGs from public/icon.svg using sharp.
 * Run: node scripts/generate-icons.mjs
 */
import { readFileSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import sharp from 'sharp'

const __dirname = dirname(fileURLToPath(import.meta.url))
const publicDir = join(__dirname, '..', 'public')

const svgBuffer = readFileSync(join(publicDir, 'icon.svg'))

async function generateIcons() {
  // Standard icons (no padding)
  await sharp(svgBuffer).resize(192, 192).png().toFile(join(publicDir, 'pwa-192x192.png'))
  console.log('✓ pwa-192x192.png')

  await sharp(svgBuffer).resize(512, 512).png().toFile(join(publicDir, 'pwa-512x512.png'))
  console.log('✓ pwa-512x512.png')

  // Apple touch icon (180x180)
  await sharp(svgBuffer).resize(180, 180).png().toFile(join(publicDir, 'apple-touch-icon-180x180.png'))
  console.log('✓ apple-touch-icon-180x180.png')

  // Maskable icon: original SVG already has safe zone padding (rounded corners + margins)
  // Use a slightly padded version for the maskable variant (the icon itself has ~19% margin built in)
  await sharp(svgBuffer).resize(512, 512).png().toFile(join(publicDir, 'maskable-icon-512x512.png'))
  console.log('✓ maskable-icon-512x512.png')

  // Favicon 64x64
  await sharp(svgBuffer).resize(64, 64).png().toFile(join(publicDir, 'favicon-64x64.png'))
  console.log('✓ favicon-64x64.png')

  console.log('Done.')
}

generateIcons().catch((err) => {
  console.error(err)
  process.exit(1)
})
