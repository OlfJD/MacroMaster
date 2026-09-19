import math
import os
from PIL import Image, ImageDraw, ImageFilter, ImageChops

def create_macromaster_icon():
    size = 1024
    # Transparent canvas
    base = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    # Squircle parameters - maximized for system tray prominence (minimal padding)
    pad = 18
    r = 230
    x0, y0, x1, y1 = pad, pad, size - pad, size - pad

    # 1. Base Dark Card
    card_mask = Image.new("L", (size, size), 0)
    cm_draw = ImageDraw.Draw(card_mask)
    cm_draw.rounded_rectangle([x0, y0, x1, y1], radius=r, fill=255)

    # Dark Gradient Background
    bg = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    bg_draw = ImageDraw.Draw(bg)
    for y in range(y0, y1 + 1):
        t = (y - y0) / max(1, (y1 - y0))
        # Top #141B2D to Bottom #080B12
        r_c = int(20 * (1 - t) + 8 * t)
        g_c = int(27 * (1 - t) + 11 * t)
        b_c = int(45 * (1 - t) + 18 * t)
        bg_draw.line([(x0, y), (x1, y)], fill=(r_c, g_c, b_c, 255))

    card = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    card.paste(bg, (0, 0), mask=card_mask)

    # 2. Ambient Neon Glow inside the card (Emerald & Cyan bloom)
    ambient_glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ag_draw = ImageDraw.Draw(ambient_glow)
    cx, cy = size // 2, size // 2

    # Draw multi-layered radial gradients
    for rad in range(460, 0, -5):
        t = rad / 460.0
        # Color transition: center vibrant cyan/emerald #10B981, outer soft blue
        alpha = int((1.0 - t)**1.7 * 160)
        r_g = int(6 * t + 16 * (1 - t))
        g_g = int(182 * t + 215 * (1 - t))
        b_g = int(212 * t + 150 * (1 - t))
        ag_draw.ellipse([cx - rad, cy - rad, cx + rad, cy + rad], fill=(r_g, g_g, b_g, alpha))

    # Mask ambient glow to card
    card_glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    card_glow.paste(ambient_glow, (0, 0), mask=card_mask)
    card = Image.alpha_composite(card, card_glow)

    # 3. Outer Border with subtle top-edge lighting
    border_layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    b_draw = ImageDraw.Draw(border_layer)
    # Dark slate frame
    b_draw.rounded_rectangle([x0, y0, x1, y1], radius=r, outline=(42, 58, 86, 255), width=18)
    # Inner subtle neon rim
    b_draw.rounded_rectangle([x0 + 5, y0 + 5, x1 - 5, y1 - 5], radius=r - 5, outline=(52, 211, 153, 110), width=6)
    card = Image.alpha_composite(card, border_layer)

    # 4. Lucide 'Zap' Vector Geometry
    # Lucide zap path in 24x24 standard: M 13 2 L 3 14 L 12 14 L 11 22 L 21 10 L 12 10 Z
    zap_coords = [
        (13, 2),
        (3, 14),
        (12, 14),
        (11, 22),
        (21, 10),
        (12, 10),
    ]

    # Scaled up to 36.5 for maximum prominence in tray and taskbar
    scale = 36.5
    ox = 512 - (12 * scale)
    oy = 512 - (12 * scale)
    zap_pts = [(int(x * scale + ox), int(y * scale + oy)) for (x, y) in zap_coords]

    # Drop shadow below the bolt
    shadow_layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    s_draw = ImageDraw.Draw(shadow_layer)
    s_draw.polygon([(x, y + 28) for (x, y) in zap_pts], fill=(0, 0, 0, 190))
    shadow_layer = shadow_layer.filter(ImageFilter.GaussianBlur(radius=32))

    # Big Neon Aura Glow
    aura_layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    a_draw = ImageDraw.Draw(aura_layer)
    a_draw.polygon(zap_pts, fill=(16, 185, 129, 255))
    aura_layer = aura_layer.filter(ImageFilter.GaussianBlur(radius=54))

    # Medium Tight Neon Glow
    aura_layer2 = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    a2_draw = ImageDraw.Draw(aura_layer2)
    a2_draw.polygon(zap_pts, fill=(52, 211, 153, 255))
    aura_layer2 = aura_layer2.filter(ImageFilter.GaussianBlur(radius=20))

    # Solid Core Bolt with Crisp Gradient & Stroke
    bolt_layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    bolt_draw = ImageDraw.Draw(bolt_layer)
    
    # Gradient fill mask for bolt
    bolt_mask = Image.new("L", (size, size), 0)
    bm_draw = ImageDraw.Draw(bolt_mask)
    bm_draw.polygon(zap_pts, fill=255)

    bolt_gradient = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    bg_fill = ImageDraw.Draw(bolt_gradient)
    min_y = min(p[1] for p in zap_pts)
    max_y = max(p[1] for p in zap_pts)
    for y in range(min_y, max_y + 1):
        ratio = (y - min_y) / max(1, (max_y - min_y))
        # Top is brilliant pure white (#FFFFFF), middle is #ECFDF5, bottom is emerald #34D399 / #10B981
        if ratio < 0.5:
            t = ratio / 0.5
            r_z = int(255 * (1 - t) + 240 * t)
            g_z = int(255 * (1 - t) + 253 * t)
            b_z = int(255 * (1 - t) + 245 * t)
        else:
            t = (ratio - 0.5) / 0.5
            r_z = int(240 * (1 - t) + 52 * t)
            g_z = int(253 * (1 - t) + 211 * t)
            b_z = int(245 * (1 - t) + 153 * t)
        bg_fill.line([(0, y), (size, y)], fill=(r_z, g_z, b_z, 255))

    bolt_core = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    bolt_core.paste(bolt_gradient, (0, 0), mask=bolt_mask)

    # Crisp Vector Outline
    bolt_stroke = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    bst_draw = ImageDraw.Draw(bolt_stroke)
    bst_draw.polygon(zap_pts, outline=(255, 255, 255, 250), width=14)

    bolt_combined = Image.alpha_composite(bolt_core, bolt_stroke)

    # 5. Assemble all layers
    result = Image.alpha_composite(card, shadow_layer)
    result = Image.alpha_composite(result, aura_layer)
    result = Image.alpha_composite(result, aura_layer2)
    result = Image.alpha_composite(result, bolt_combined)

    return result

if __name__ == "__main__":
    os.makedirs("Assets", exist_ok=True)
    icon_img = create_macromaster_icon()
    icon_img.save("Assets/app.png", format="PNG")
    
    # High-quality multi-res ICO including Windows High-DPI tray sizes (16, 20, 24, 32, 48, 64, 128, 256)
    sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (24, 24), (20, 20), (16, 16)]
    icon_img.save("Assets/app.ico", format="ICO", sizes=sizes)
    print("Generated Assets/app.png and Assets/app.ico successfully!")
