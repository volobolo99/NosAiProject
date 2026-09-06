#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
nosapki_scraper.py -- scraper reale per https://nosapki.com/it (community database
italiano di NosTale) per NosAiProject.

Effettua richieste HTTP reali (nessun mock/fixture) e fa parsing dell'HTML reale
ricevuto con BeautifulSoup/lxml. Nessun valore e' inventato: un campo assente o
non parsabile in una pagina viene salvato come `null` nel JSON di output.

USO
---
    python3 nosapki_scraper.py --out-dir <directory_output> [opzioni]

Esempi:
    # Export di default: liste complete + campione dettagli item/mostri (100+100)
    # + alcune categorie quest reali via AJAX + dettaglio di tutte le mappe.
    python3 nosapki_scraper.py --out-dir /tmp/nosapki_export

    # Solo le liste (item/mostri/compagni/pet/mappe/skill), niente pagine dettaglio,
    # utile per un giro veloce di validazione struttura.
    python3 nosapki_scraper.py --out-dir /tmp/nosapki_export \
        --item-detail-sample 0 --monster-detail-sample 0 --map-details 0 \
        --quest-categories 0

    # Copertura piu' ampia dei dettagli (piu' lento, piu' richieste al sito).
    python3 nosapki_scraper.py --out-dir /tmp/nosapki_export \
        --item-detail-sample 400 --monster-detail-sample 400 --quest-categories 10

OPZIONI PRINCIPALI (vedi anche --help)
    --out-dir                 directory di output per i file JSON (obbligatoria)
    --rate-min / --rate-max   secondi di attesa casuale tra due richieste consecutive
                               (default 0.6 / 1.1 -- cortesia verso il sito esterno)
    --item-detail-sample      numero di pagine dettaglio item da scaricare, campionate
                               in modo uniforme sull'intero elenco (default 100; 0 = nessuna)
    --monster-detail-sample   numero di pagine dettaglio mostro da scaricare, campionate
                               in modo uniforme (default 100; 0 = nessuna)
    --map-details             1 per scaricare anche la pagina dettaglio di ogni mappa
                               (default 1; l'elenco mappe e' piccolo, si scarica per intero)
    --quest-categories        numero di categorie quest reali da interrogare via l'endpoint
                               AJAX POST /it/quests/get_category (default 6; 0 = nessuna)
    --max-list-pages          tetto di sicurezza sul numero di pagine per singola sezione
                               a paginazione numerica, per non girare all'infinito in caso
                               di comportamento imprevisto del sito (default 60)
    --user-agent              User-Agent onesto inviato in ogni richiesta
    --timeout                 timeout per richiesta in secondi (default 20)

OUTPUT
------
Nella directory indicata da --out-dir vengono scritti (UTF-8, indentati):
    items.json, monsters.json, partners.json, pets.json, maps.json, skills.json,
    quests.json, exp_curves.json, run_report.json

`run_report.json` riporta conteggi reali, richieste HTTP totali ed eventuali errori
per sezione (pagine saltate, parsing falliti, timeout) -- nessun errore viene
nascosto producendo dati vuoti o inventati al suo posto.

CORTESIA VERSO IL SITO
-----------------------
Le richieste sono sequenziali (mai parallele), con un ritardo casuale configurabile
tra una richiesta e la successiva, e un User-Agent che identifica lo scraper e un
contatto. Rispetta `robots.txt` (verificato manualmente: `Disallow:` vuoto per
tutti gli user-agent al 2026-09-06).
"""

from __future__ import annotations

import argparse
import json
import random
import re
import sys
import time
from dataclasses import dataclass, field
from typing import Any, Optional
from urllib.parse import unquote, urlencode

try:
    import requests
except ImportError:  # pragma: no cover - ambiente senza requests
    print("ERRORE: il pacchetto 'requests' non e' installato. Esegui: pip install requests", file=sys.stderr)
    raise

try:
    from bs4 import BeautifulSoup
except ImportError:  # pragma: no cover - ambiente senza bs4
    print(
        "ERRORE: il pacchetto 'beautifulsoup4' non e' installato. "
        "Esegui: pip install beautifulsoup4 lxml",
        file=sys.stderr,
    )
    raise

BASE = "https://nosapki.com"
DEFAULT_UA = (
    "NosAiProjectResearchBot/1.0 "
    "(+educational/research data collection for a NosTale AI player project; "
    "contact: volobolo99@gmail.com)"
)


# --------------------------------------------------------------------------- #
# HTTP layer: rate-limited, sequential, con log onesto degli errori.
# --------------------------------------------------------------------------- #


class Fetcher:
    """Wrapper su requests.Session con rate-limit e log errori onesto.

    Nessuna richiesta parallela: una sessione, una richiesta alla volta, con
    attesa casuale tra `rate_min` e `rate_max` secondi dopo ogni risposta.
    """

    def __init__(
        self,
        user_agent: str,
        rate_min: float,
        rate_max: float,
        timeout: float,
        max_retries: int = 3,
    ) -> None:
        self.session = requests.Session()
        self.session.headers.update(
            {
                "User-Agent": user_agent,
                "Accept-Language": "it-IT,it;q=0.9,en;q=0.5",
            }
        )
        self.rate_min = rate_min
        self.rate_max = rate_max
        self.timeout = timeout
        self.max_retries = max_retries
        self.request_count = 0
        self.errors: list[dict[str, Any]] = []

    def _sleep(self) -> None:
        time.sleep(random.uniform(self.rate_min, self.rate_max))

    def _request(self, method: str, url: str, **kwargs) -> Optional[requests.Response]:
        last_error: Optional[str] = None
        for attempt in range(1, self.max_retries + 1):
            try:
                resp = self.session.request(method, url, timeout=self.timeout, **kwargs)
                self.request_count += 1
                self._sleep()
                if resp.status_code >= 500 and attempt < self.max_retries:
                    time.sleep(1.5 * attempt)
                    continue
                if resp.status_code != 200:
                    self.errors.append(
                        {
                            "url": url,
                            "method": method,
                            "http_status": resp.status_code,
                            "attempt": attempt,
                        }
                    )
                return resp
            except requests.RequestException as exc:
                last_error = str(exc)
                time.sleep(1.0 * attempt)
        self.errors.append({"url": url, "method": method, "error": last_error})
        return None

    def get(self, url: str, **kwargs) -> Optional[requests.Response]:
        return self._request("GET", url, **kwargs)

    def post(self, url: str, **kwargs) -> Optional[requests.Response]:
        return self._request("POST", url, **kwargs)

    def get_soup(self, url: str, **kwargs) -> Optional[BeautifulSoup]:
        resp = self.get(url, **kwargs)
        if resp is None or resp.status_code != 200:
            return None
        return BeautifulSoup(resp.text, "lxml")

    def xsrf_token(self) -> Optional[str]:
        for cookie in self.session.cookies:
            if cookie.name == "XSRF-TOKEN":
                return unquote(cookie.value)
        return None


# --------------------------------------------------------------------------- #
# Helper di parsing generici.
# --------------------------------------------------------------------------- #


def parse_int(text: Any) -> Optional[int]:
    """Estrae un intero da un testo italiano (spazi come separatore migliaia,
    suffissi come 's' per i secondi). Ritorna None se non c'e' nulla di numerico
    -- mai un valore indovinato."""
    if text is None:
        return None
    if isinstance(text, (int,)):
        return text
    s = re.sub(r"[^\d-]", "", str(text))
    if s in ("", "-"):
        return None
    try:
        return int(s)
    except ValueError:
        return None


def text_or_none(el) -> Optional[str]:
    return el.get_text(strip=True) if el is not None else None


def clean_html_artifact_name(text: Optional[str]) -> Optional[str]:
    """Il sito incorpora talvolta un tag <br> letterale in fondo agli attributi
    data-name (artefatto del template, non parte del nome reale). Lo rimuoviamo
    senza toccare il resto del testo -- non e' un'invenzione di dati, e' pulizia
    di un tag HTML residuo verificabile nel markup sorgente."""
    if text is None:
        return None
    return re.sub(r"(<br\s*/?>)+$", "", text).strip() or None


def clean(url: Optional[str]) -> Optional[str]:
    return url


# --------------------------------------------------------------------------- #
# Scoperta categorie (dal menu di navigazione, presente su ogni pagina).
# --------------------------------------------------------------------------- #


def discover_categories(nav_soup: BeautifulSoup, section: str) -> "dict[str, str]":
    """Ritorna {slug_categoria: etichetta} scoperto dal menu reale del sito,
    per la sezione indicata ('items' o 'skills'). Non hard-codiamo l'elenco:
    lo leggiamo dal markup reale ad ogni esecuzione."""
    out: dict[str, str] = {}
    for a in nav_soup.select(f'a[href*="/it/{section}?category="]'):
        href = a.get("href") or ""
        m = re.search(r"category=([^&]+)", href)
        if not m:
            continue
        slug = m.group(1)
        if slug.isdigit():
            # href di paginazione tipo category=1&page=2, non una categoria del menu
            continue
        if slug in out:
            continue
        label_el = a.select_one("p.text")
        out[slug] = text_or_none(label_el) or a.get_text(strip=True)
    return out


def discover_quest_categories(quests_soup: BeautifulSoup) -> "list[dict[str, Any]]":
    """Ritorna le categorie quest reali (id numerico, etichetta, se e' un
    raggruppamento 'show-sub' o una foglia selezionabile) lette dal markup
    reale di /it/quests."""
    out = []
    for span in quests_soup.select("span[data-id]"):
        raw_id = span.get("data-id")
        try:
            cid = int(raw_id)
        except (TypeError, ValueError):
            continue
        if cid < 0:
            continue
        classes = span.get("class") or []
        out.append(
            {
                "id": cid,
                "label": span.get_text(strip=True),
                "is_group": "show-sub" in classes,
            }
        )
    return out


# --------------------------------------------------------------------------- #
# Liste paginate (item, skill: markup a.item-box / mostri, compagni, pet: a.monster-box).
# --------------------------------------------------------------------------- #


def parse_item_box_list(soup: BeautifulSoup, id_prefix: str) -> "list[dict[str, Any]]":
    out = []
    pattern = re.compile(rf"{re.escape(id_prefix)}/(\d+)")
    for a in soup.select("a.item-box"):
        href = a.get("href") or ""
        m = pattern.search(href)
        if not m:
            continue
        name_el = a.select_one("p")
        icon_el = a.select_one("img")
        out.append(
            {
                "id": int(m.group(1)),
                "name": text_or_none(name_el),
                "icon": clean(icon_el.get("src")) if icon_el is not None else None,
            }
        )
    return out


def parse_monster_box_list(soup: BeautifulSoup, id_prefix: str) -> "list[dict[str, Any]]":
    out = []
    pattern = re.compile(rf"{re.escape(id_prefix)}/(\d+)")
    for a in soup.select("a.monster-box"):
        href = a.get("href") or ""
        m = pattern.search(href)
        if not m:
            continue
        name_el = a.select_one("p.monster-name")
        level_el = None
        for p in a.select("p"):
            if "monster-name" in (p.get("class") or []):
                continue
            level_el = p
            break
        icon_el = a.select_one("img.main-image")
        top_icons = a.select(".top-right img")
        out.append(
            {
                "id": int(m.group(1)),
                "name": text_or_none(name_el),
                "level": parse_int(level_el.get_text()) if level_el is not None else None,
                "icon": clean(icon_el.get("src")) if icon_el is not None else None,
                "category_icon": clean(top_icons[0].get("src")) if len(top_icons) > 0 else None,
                "element_icon": clean(top_icons[1].get("src")) if len(top_icons) > 1 else None,
            }
        )
    return out


def parse_maps_list(soup: BeautifulSoup) -> "list[dict[str, Any]]":
    out = []
    pattern = re.compile(r"/it/maps/(\d+)$")
    for a in soup.select("div.listing-maps a[href]"):
        href = a.get("href") or ""
        m = pattern.search(href)
        if not m:
            continue
        name_el = a.select_one("p")
        out.append({"id": int(m.group(1)), "name": text_or_none(name_el)})
    return out


def scrape_paginated_list(
    fetcher: Fetcher,
    url_template: str,
    parse_fn,
    max_pages: int,
    section_label: str,
    errors: list,
    max_consecutive_failures: int = 4,
) -> "list[dict[str, Any]]":
    """Scarica pagine 1..N di una lista finche' una pagina non produce piu'
    voci nuove (fine reale della paginazione) o si raggiunge il tetto di
    sicurezza `max_pages`.

    Un singolo fallimento di rete su una pagina (timeout, connessione
    interrotta) NON abbandona l'intera sezione: viene registrato in `errors`
    e si prova la pagina successiva, fino a `max_consecutive_failures`
    fallimenti di fila -- solo a quel punto si considera la sezione
    realmente irraggiungibile. Questo evita che un blip di rete transitorio
    azzeri silenziosamente centinaia di voci reali."""
    seen_ids: set[int] = set()
    entries: list[dict[str, Any]] = []
    page = 1
    consecutive_failures = 0
    while page <= max_pages:
        url = url_template.format(page=page)
        soup = fetcher.get_soup(url)
        if soup is None:
            consecutive_failures += 1
            errors.append({"section": section_label, "page": page, "url": url, "error": "fetch fallita"})
            if consecutive_failures >= max_consecutive_failures:
                errors.append(
                    {
                        "section": section_label,
                        "page": page,
                        "error": f"sezione interrotta dopo {consecutive_failures} fallimenti di rete consecutivi",
                    }
                )
                break
            time.sleep(2.0 * consecutive_failures)
            page += 1
            continue
        consecutive_failures = 0
        page_entries = parse_fn(soup)
        if not page_entries:
            break
        new_entries = [e for e in page_entries if e["id"] not in seen_ids]
        for e in new_entries:
            seen_ids.add(e["id"])
            entries.append(e)
        if not new_entries and page > 1:
            # pagina ripetuta rispetto alla precedente: fine reale della lista
            break
        page += 1
    return entries


# --------------------------------------------------------------------------- #
# Pagine di dettaglio item.
# --------------------------------------------------------------------------- #


def parse_item_detail(soup: BeautifulSoup) -> Optional[dict[str, Any]]:
    box = soup.select_one("div.box-einfo")
    if box is None:
        return None

    def text_of(selector: str) -> Optional[str]:
        return text_or_none(box.select_one(selector))

    data: dict[str, Any] = {
        "name": text_of("p.name"),
        "class_requirement": text_of("p.class"),
        "level_required_raw": text_of("p.level"),
        "rarity_raw": text_of("p.rarity"),
        "attack_raw": text_of("p.attack"),
        "hit_rate_raw": text_of("p.accuracy"),
        "crit_raw": text_of("p.crit"),
        "cost_raw": text_of("p.cost"),
        "description": text_of("p.descr"),
    }

    level_m = re.search(r"(\d+)", data["level_required_raw"] or "")
    data["level_required"] = int(level_m.group(1)) if level_m else None

    rarity_m = re.search(r"(\d+)", data["rarity_raw"] or "")
    data["rarity"] = int(rarity_m.group(1)) if rarity_m else None

    dmg_m = re.search(r"(\d+)\s*~\s*(\d+)", data["attack_raw"] or "")
    if dmg_m:
        data["damage_min"] = int(dmg_m.group(1))
        data["damage_max"] = int(dmg_m.group(2))
    else:
        data["damage_min"] = None
        data["damage_max"] = None

    data["hit_rate"] = parse_int(data["hit_rate_raw"])
    data["cost"] = parse_int(data["cost_raw"])

    crit_m = re.search(r"(\d+)%.*?(\d+)", data["crit_raw"] or "")
    if crit_m:
        data["crit_chance_percent"] = int(crit_m.group(1))
        data["crit_value"] = int(crit_m.group(2))
    else:
        data["crit_chance_percent"] = None
        data["crit_value"] = None

    return data


# --------------------------------------------------------------------------- #
# Pagine di dettaglio mostro (usate anche per compagni/pet dove il markup coincide).
# --------------------------------------------------------------------------- #


def _section_pairs(container) -> "list[tuple[str, Any]]":
    """Ritorna coppie (etichetta, nodo <div class='content'>) per ogni
    <p class='sub-title'> dentro il container dato."""
    pairs = []
    for sub in container.select("p.sub-title"):
        content = sub.find_next_sibling("div", class_="content")
        if content is None:
            continue
        pairs.append((sub.get_text(strip=True), content))
    return pairs


def parse_monster_detail(soup: BeautifulSoup) -> Optional[dict[str, Any]]:
    content_root = soup.select_one("#divStrona")
    if content_root is None:
        return None

    h1 = content_root.select_one("h1")
    result: dict[str, Any] = {"name": text_or_none(h1)}

    # Raggruppa i nodi p.title -> il loro contenitore diretto (half-background),
    # cosi' possiamo leggere le coppie sub-title/content di ogni sezione senza
    # confonderle con quelle del menu di navigazione (fuori da #divStrona).
    sections: dict[str, list] = {}
    for title_p in content_root.select("p.title"):
        title = title_p.get_text(strip=True)
        sections.setdefault(title, []).append(title_p.parent)

    def section_raw(name: str) -> dict[str, str]:
        raw: dict[str, str] = {}
        for parent in sections.get(name, []):
            for label, content in _section_pairs(parent):
                raw[label] = content.get_text(" ", strip=True)
        return raw

    stats_raw = section_raw("Statistiche principali")
    attack_raw = section_raw("Attacco")
    defense_raw = section_raw("Difesa")

    result["stats_raw"] = stats_raw
    result["attack_raw"] = attack_raw
    result["defense_raw"] = defense_raw

    result["level"] = parse_int(stats_raw.get("Livello"))
    result["category"] = stats_raw.get("Categoria")
    result["hp"] = parse_int(stats_raw.get("HP"))
    result["mp"] = parse_int(stats_raw.get("MP"))
    result["movement_speed"] = parse_int(stats_raw.get("Velocita' di movimento") or stats_raw.get("Velocità di movimento"))
    result["regen_seconds"] = parse_int(stats_raw.get("Rigenerazione"))
    result["exp"] = parse_int(stats_raw.get("Punti esperienza"))
    result["exp_job"] = parse_int(stats_raw.get("Punti esperienza lavoro"))

    result["hit_rate"] = parse_int(attack_raw.get("HitRate"))
    result["aggressiveness"] = attack_raw.get("Aggressivita'") or attack_raw.get("Aggressività")
    result["evasion"] = parse_int(defense_raw.get("Elusione"))

    # Effetti speciali: sezione titolata 'Effetti' o 'Speciale' a seconda del mostro.
    special_effects = []
    for sec_name in ("Effetti", "Speciale"):
        for parent in sections.get(sec_name, []):
            for trigger, content in _section_pairs(parent):
                for p in content.select("p"):
                    txt = p.get_text(" ", strip=True)
                    if txt:
                        special_effects.append({"trigger": trigger, "text": txt})
    result["special_effects"] = special_effects

    # Drop: sezione 'Oggetti'.
    drops = []
    for parent in sections.get("Oggetti", []):
        for a in parent.select("a.item-amount-percent"):
            percent_el = a.select_one("p.percent")
            amount_el = a.select_one("p.amount")
            drops.append(
                {
                    "item_id": parse_int(a.get("data-id")),
                    "item_name": clean_html_artifact_name(a.get("data-name")),
                    "amount": parse_int(a.get("data-amount")) or parse_int(text_or_none(amount_el)),
                    "chance_percent_raw": text_or_none(percent_el),
                }
            )
    result["drops"] = drops

    # Mappe di spawn con coordinate: div.window-minimap con data-id + minimap-point.
    spawn_maps = []
    for wm in content_root.select("div.window-minimap"):
        name_el = wm.select_one(".map-name")
        points_amount_el = wm.select_one(".map-points-amount")
        points = []
        for pt in wm.select(".minimap-point"):
            style = pt.get("style") or ""
            xm = re.search(r"left:\s*([\d.]+)px", style)
            ym = re.search(r"top:\s*([\d.-]+)px", style)
            points.append(
                {
                    "x": float(xm.group(1)) if xm else None,
                    "y": float(ym.group(1)) if ym else None,
                }
            )
        spawn_maps.append(
            {
                "map_id": parse_int(wm.get("data-id")),
                "map_name": text_or_none(name_el),
                "point_count": parse_int(text_or_none(points_amount_el)) if points_amount_el is not None else len(points),
                "points": points,
            }
        )
    result["spawn_maps"] = spawn_maps

    # Riferimenti a spazio-tempo (found-in-box) con range di livello del gruppo.
    timespace_refs = []
    for a in content_root.select("a.found-in-box"):
        p = a.select_one("p")
        timespace_refs.append({"url": a.get("href"), "level_range_raw": text_or_none(p)})
    result["timespace_refs"] = timespace_refs

    return result


# --------------------------------------------------------------------------- #
# Pagine di dettaglio mappa.
# --------------------------------------------------------------------------- #


def parse_map_detail(soup: BeautifulSoup) -> Optional[dict[str, Any]]:
    content_root = soup.select_one("#divStrona")
    if content_root is None:
        return None

    h1 = content_root.select_one("h1")
    result: dict[str, Any] = {"name": text_or_none(h1)}

    wm = content_root.select_one("div.window-minimap")
    portals = []
    if wm is not None:
        for a in wm.select("a.minimap-portal"):
            href = a.get("href") or ""
            m = re.search(r"/it/maps/(\d+)", href)
            style = a.get("style") or ""
            xm = re.search(r"left:\s*([\d.]+)px", style)
            ym = re.search(r"top:\s*([\d.-]+)px", style)
            portals.append(
                {
                    "target_map_id": int(m.group(1)) if m else None,
                    "target_map_name": a.get("data-name"),
                    "x": float(xm.group(1)) if xm else None,
                    "y": float(ym.group(1)) if ym else None,
                }
            )
    result["portals"] = portals

    def entity_list(section_title: str) -> "list[dict[str, Any]]":
        out = []
        for half in content_root.select("div.half-background"):
            title_el = half.select_one("p.title")
            if title_el is None or title_el.get_text(strip=True) != section_title:
                continue
            for a in half.select("a.monster-icon-name"):
                href = a.get("href") or ""
                m = re.search(r"/(\d+)$", href)
                name_el = a.select_one(".monster-icon-name-title")
                value_el = a.select_one(".monster-icon-name-value")
                out.append(
                    {
                        "id": int(m.group(1)) if m else None,
                        "name": text_or_none(name_el),
                        "count_raw": text_or_none(value_el),
                        "count": parse_int(text_or_none(value_el)),
                    }
                )
        return out

    result["monsters"] = entity_list("Mostri")
    result["npcs"] = entity_list("NPC")
    return result


# --------------------------------------------------------------------------- #
# Quest via endpoint AJAX POST /it/quests/get_category.
# --------------------------------------------------------------------------- #


def parse_quest_category_html(html: str) -> "list[dict[str, Any]]":
    soup = BeautifulSoup(html, "lxml")
    out = []
    for row in soup.select("div.quest-row"):
        title_el = row.select_one("span.quest-title")
        desc_el = row.select_one("span.quest-description")
        steps = []
        for li in row.select("div.quest-datas li"):
            steps.append(li.get_text(" ", strip=True))
        prizes = []
        for a in row.select("div.quest-prizes a"):
            prizes.append(
                {
                    "type": a.get("data-element-type"),
                    "id": parse_int(a.get("data-id")),
                    "name": clean_html_artifact_name(a.get("data-name")),
                }
            )
        # riferimenti incrociati (mostro/mappa/npc/item) citati nei passi della quest
        references = []
        for ref in row.select(".quest-element, a.quest-element"):
            references.append(
                {
                    "kind": next((c for c in (ref.get("class") or []) if c != "quest-element"), None),
                    "id": parse_int(ref.get("data-id")),
                    "text": ref.get_text(strip=True),
                }
            )
        out.append(
            {
                "quest_id": parse_int(row.get("data-id")),
                "title": text_or_none(title_el),
                "description": text_or_none(desc_el),
                "steps": steps,
                "prizes": prizes,
                "references": references,
            }
        )
    return out


def extract_exp_curves(quests_page_html: str) -> "dict[str, Any]":
    curves = {}
    for name in ("exp_level", "exp_levelh", "exp_job", "exp_jobsp", "exp_jobspfun", "exp_boost", "exp_event"):
        m = re.search(rf"\b{name}\s*=\s*(\[[^;]*\]);", quests_page_html)
        if not m:
            curves[name] = None
            continue
        try:
            curves[name] = json.loads(m.group(1))
        except json.JSONDecodeError:
            curves[name] = None
    return curves


# --------------------------------------------------------------------------- #
# Orchestrazione.
# --------------------------------------------------------------------------- #


def run_list_section_with_retry(
    fetcher: Fetcher,
    url_template: str,
    parse_fn,
    max_pages: int,
    section_label: str,
    errors: list,
) -> "list[dict[str, Any]]":
    """Chiama scrape_paginated_list; se il risultato e' vuoto ma la sezione ha
    prodotto errori di rete (quindi il vuoto non e' una lista reale vuota),
    ritenta l'intera sezione una sola volta dopo una pausa piu' lunga. Difesa
    ulteriore contro un blip di rete che colpisce anche il primo tentativo
    sulla prima pagina di una sezione."""
    errors_before = len(errors)
    entries = scrape_paginated_list(fetcher, url_template, parse_fn, max_pages, section_label, errors)
    had_errors = len(errors) > errors_before
    if not entries and had_errors:
        errors.append({"section": section_label, "note": "risultato vuoto con errori di rete: ritento l'intera sezione"})
        time.sleep(5.0)
        entries = scrape_paginated_list(fetcher, url_template, parse_fn, max_pages, section_label, errors)
    return entries


def evenly_spaced_sample(items: list, k: int) -> list:
    if k <= 0 or not items:
        return []
    if k >= len(items):
        return list(items)
    step = len(items) / k
    idxs = sorted({int(i * step) for i in range(k)})
    return [items[i] for i in idxs]


@dataclass
class RunReport:
    started_at: str
    sections: dict = field(default_factory=dict)
    total_requests: int = 0
    global_errors: list = field(default_factory=list)


def write_json(out_dir: str, filename: str, data: Any) -> None:
    import os

    path = os.path.join(out_dir, filename)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print(f"  scritto {path} ({len(json.dumps(data))} byte)")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Scraper reale per nosapki.com/it (community database italiano NosTale).",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument("--out-dir", required=True, help="Directory di output per i JSON esportati.")
    parser.add_argument("--rate-min", type=float, default=0.6, help="Attesa minima (s) tra le richieste.")
    parser.add_argument("--rate-max", type=float, default=1.1, help="Attesa massima (s) tra le richieste.")
    parser.add_argument("--timeout", type=float, default=20.0, help="Timeout per richiesta (s).")
    parser.add_argument("--user-agent", default=DEFAULT_UA, help="User-Agent onesto da inviare.")
    parser.add_argument("--max-list-pages", type=int, default=60, help="Tetto di sicurezza pagine per sezione.")
    parser.add_argument("--item-detail-sample", type=int, default=100, help="N. pagine dettaglio item (campione uniforme).")
    parser.add_argument("--monster-detail-sample", type=int, default=100, help="N. pagine dettaglio mostro (campione uniforme).")
    parser.add_argument("--map-details", type=int, default=1, help="1 per scaricare anche il dettaglio di ogni mappa.")
    parser.add_argument("--quest-categories", type=int, default=6, help="N. categorie quest reali da scaricare via AJAX.")
    args = parser.parse_args()

    import os

    os.makedirs(args.out_dir, exist_ok=True)

    fetcher = Fetcher(args.user_agent, args.rate_min, args.rate_max, args.timeout)
    report = RunReport(started_at=time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()))

    print(f"[nosapki_scraper] output -> {args.out_dir}")
    print(f"[nosapki_scraper] User-Agent: {args.user_agent}")

    # ---- 1. Scoperta categorie reali dal menu di navigazione -------------- #
    print("[1/8] Scoperta categorie item/skill dal menu di navigazione reale...")
    nav_soup = fetcher.get_soup(f"{BASE}/it")
    if nav_soup is None:
        print("ERRORE FATALE: impossibile caricare la home page per leggere il menu. Interrompo.", file=sys.stderr)
        return 1
    item_categories = discover_categories(nav_soup, "items")
    skill_categories = discover_categories(nav_soup, "skills")
    print(f"  categorie item scoperte: {len(item_categories)}")
    print(f"  categorie skill scoperte: {len(skill_categories)}")
    report.sections["categories"] = {
        "item_categories_found": len(item_categories),
        "skill_categories_found": len(skill_categories),
    }

    # ---- 2. Item: liste per categoria -------------------------------------#
    print("[2/8] Scraping liste item per categoria...")
    items_by_id: dict[int, dict[str, Any]] = {}
    item_errors: list = []
    for slug, label in item_categories.items():
        url_template = f"{BASE}/it/items?category={slug}&page={{page}}"
        entries = run_list_section_with_retry(
            fetcher, url_template, lambda s: parse_item_box_list(s, "/it/items"), args.max_list_pages, f"items:{slug}", item_errors
        )
        for e in entries:
            e.setdefault("categories", [])
            if slug not in items_by_id.get(e["id"], {}).get("categories", []):
                if e["id"] in items_by_id:
                    if slug not in items_by_id[e["id"]]["categories"]:
                        items_by_id[e["id"]]["categories"].append(slug)
                else:
                    e["categories"] = [slug]
                    e["category_labels"] = [label]
                    items_by_id[e["id"]] = e
    print(f"  item unici in elenco: {len(items_by_id)} (su {len(item_categories)} categorie)")

    # ---- 3. Mostri, compagni, pet: liste paginate -------------------------#
    print("[3/8] Scraping liste mostri/compagni/pet...")
    monster_errors: list = []
    monsters_list = run_list_section_with_retry(
        fetcher, f"{BASE}/it/npcs/monsters?page={{page}}", lambda s: parse_monster_box_list(s, "/it/npcs/monsters"), args.max_list_pages, "monsters", monster_errors
    )
    partners_list = run_list_section_with_retry(
        fetcher, f"{BASE}/it/npcs/partners?page={{page}}", lambda s: parse_monster_box_list(s, "/it/npcs/partners"), args.max_list_pages, "partners", monster_errors
    )
    pets_list = run_list_section_with_retry(
        fetcher, f"{BASE}/it/npcs/pets?page={{page}}", lambda s: parse_monster_box_list(s, "/it/npcs/pets"), args.max_list_pages, "pets", monster_errors
    )
    print(f"  mostri: {len(monsters_list)}  compagni: {len(partners_list)}  pet: {len(pets_list)}")

    # ---- 4. Mappe: lista + dettaglio (elenco piccolo, si scarica intero) -- #
    print("[4/8] Scraping lista mappe...")
    map_errors: list = []
    maps_list = run_list_section_with_retry(
        fetcher, f"{BASE}/it/maps?page={{page}}", parse_maps_list, args.max_list_pages, "maps", map_errors
    )
    print(f"  mappe in elenco: {len(maps_list)}")

    maps_detail: dict[int, Any] = {}
    if args.map_details:
        print(f"  scarico dettaglio per tutte le {len(maps_list)} mappe...")
        for i, m in enumerate(maps_list, 1):
            soup = fetcher.get_soup(f"{BASE}/it/maps/{m['id']}")
            if soup is None:
                map_errors.append({"section": "maps_detail", "map_id": m["id"], "error": "fetch fallita"})
                continue
            detail = parse_map_detail(soup)
            if detail is None:
                map_errors.append({"section": "maps_detail", "map_id": m["id"], "error": "parsing fallito (markup inatteso)"})
                continue
            maps_detail[m["id"]] = detail
            if i % 10 == 0:
                print(f"    ...{i}/{len(maps_list)} mappe dettaglio scaricate")

    # ---- 5. Skill: liste per categoria ------------------------------------#
    print("[5/8] Scraping liste skill per categoria...")
    skills_by_id: dict[int, dict[str, Any]] = {}
    skill_errors: list = []
    for slug, label in skill_categories.items():
        url_template = f"{BASE}/it/skills?category={slug}&page={{page}}"
        entries = run_list_section_with_retry(
            fetcher, url_template, lambda s: parse_item_box_list(s, "/it/skills"), args.max_list_pages, f"skills:{slug}", skill_errors
        )
        for e in entries:
            if e["id"] in skills_by_id:
                if slug not in skills_by_id[e["id"]]["categories"]:
                    skills_by_id[e["id"]]["categories"].append(slug)
            else:
                e["categories"] = [slug]
                e["category_labels"] = [label]
                skills_by_id[e["id"]] = e
    print(f"  skill uniche in elenco: {len(skills_by_id)} (su {len(skill_categories)} categorie)")

    # ---- 6. Campione pagine dettaglio item/mostri -------------------------#
    print("[6/8] Scraping campione pagine dettaglio item...")
    item_detail_errors: list = []
    sample_item_ids = evenly_spaced_sample(sorted(items_by_id.keys()), args.item_detail_sample)
    for i, item_id in enumerate(sample_item_ids, 1):
        soup = fetcher.get_soup(f"{BASE}/it/items/{item_id}")
        if soup is None:
            item_detail_errors.append({"item_id": item_id, "error": "fetch fallita"})
            continue
        detail = parse_item_detail(soup)
        if detail is None:
            item_detail_errors.append({"item_id": item_id, "error": "parsing fallito (markup inatteso)"})
            continue
        items_by_id[item_id]["detail"] = detail
        if i % 20 == 0:
            print(f"    ...{i}/{len(sample_item_ids)} dettagli item scaricati")
    print(f"  dettagli item scaricati con successo: {len(sample_item_ids) - len(item_detail_errors)}/{len(sample_item_ids)}")

    print("[6/8] Scraping campione pagine dettaglio mostri...")
    monster_detail_errors: list = []
    monster_ids = sorted({m["id"] for m in monsters_list})
    sample_monster_ids = evenly_spaced_sample(monster_ids, args.monster_detail_sample)
    monsters_detail: dict[int, Any] = {}
    for i, monster_id in enumerate(sample_monster_ids, 1):
        soup = fetcher.get_soup(f"{BASE}/it/npcs/monsters/{monster_id}")
        if soup is None:
            monster_detail_errors.append({"monster_id": monster_id, "error": "fetch fallita"})
            continue
        detail = parse_monster_detail(soup)
        if detail is None:
            monster_detail_errors.append({"monster_id": monster_id, "error": "parsing fallito (markup inatteso)"})
            continue
        monsters_detail[monster_id] = detail
        if i % 20 == 0:
            print(f"    ...{i}/{len(sample_monster_ids)} dettagli mostro scaricati")
    print(f"  dettagli mostro scaricati con successo: {len(sample_monster_ids) - len(monster_detail_errors)}/{len(sample_monster_ids)}")

    # ---- 7. Quest via endpoint AJAX (CSRF Laravel) ------------------------#
    print("[7/8] Scraping quest reali via endpoint AJAX (CSRF Laravel)...")
    quest_errors: list = []
    quests_page = fetcher.get(f"{BASE}/it/quests")
    quests_data: dict[str, Any] = {}
    exp_curves: dict[str, Any] = {}
    quest_category_defs: list = []
    if quests_page is None or quests_page.status_code != 200:
        quest_errors.append({"error": "impossibile caricare /it/quests per ottenere il cookie CSRF"})
    else:
        quests_soup = BeautifulSoup(quests_page.text, "lxml")
        quest_category_defs = discover_quest_categories(quests_soup)
        exp_curves = extract_exp_curves(quests_page.text)
        xsrf = fetcher.xsrf_token()
        if not xsrf:
            quest_errors.append({"error": "cookie XSRF-TOKEN assente dopo GET /it/quests"})
        else:
            leaves = [c for c in quest_category_defs if not c["is_group"]]
            chosen = evenly_spaced_sample(leaves, args.quest_categories)
            for cat in chosen:
                resp = fetcher.post(
                    f"{BASE}/it/quests/get_category",
                    data=urlencode({"category": cat["id"]}),
                    headers={
                        "X-XSRF-TOKEN": xsrf,
                        "X-Requested-With": "XMLHttpRequest",
                        "Content-Type": "application/x-www-form-urlencoded",
                        "Referer": f"{BASE}/it/quests",
                        "Accept": "application/json",
                    },
                )
                if resp is None or resp.status_code != 200:
                    quest_errors.append({"category_id": cat["id"], "label": cat["label"], "error": "richiesta POST fallita"})
                    continue
                try:
                    payload = resp.json()
                except ValueError:
                    quest_errors.append({"category_id": cat["id"], "label": cat["label"], "error": "risposta non JSON"})
                    continue
                if payload.get("status") != "ok":
                    quest_errors.append(
                        {"category_id": cat["id"], "label": cat["label"], "error": f"status non ok: {payload.get('status')}"}
                    )
                    continue
                quests = parse_quest_category_html(payload.get("html", ""))
                quests_data[str(cat["id"])] = {"label": cat["label"], "quests": quests}
                print(f"  categoria '{cat['label']}' (id={cat['id']}): {len(quests)} quest estratte")
    print(f"  categorie quest scaricate: {len(quests_data)}")

    # ---- 8. Scrittura output ------------------------------------------- #
    print("[8/8] Scrittura file JSON di output...")
    write_json(args.out_dir, "items.json", {"categories": item_categories, "items": list(items_by_id.values())})
    write_json(
        args.out_dir,
        "monsters.json",
        {"monsters": monsters_list, "details": {str(k): v for k, v in monsters_detail.items()}},
    )
    write_json(args.out_dir, "partners.json", {"partners": partners_list})
    write_json(args.out_dir, "pets.json", {"pets": pets_list})
    write_json(
        args.out_dir,
        "maps.json",
        {"maps": maps_list, "details": {str(k): v for k, v in maps_detail.items()}},
    )
    write_json(args.out_dir, "skills.json", {"categories": skill_categories, "skills": list(skills_by_id.values())})
    write_json(
        args.out_dir,
        "quests.json",
        {"category_tree": quest_category_defs, "categories_downloaded": quests_data},
    )
    write_json(args.out_dir, "exp_curves.json", exp_curves)

    report.total_requests = fetcher.request_count
    report.global_errors = fetcher.errors
    report.sections.update(
        {
            "items": {
                "categories_scraped": len(item_categories),
                "unique_items_listed": len(items_by_id),
                "detail_pages_attempted": len(sample_item_ids),
                "detail_pages_ok": len(sample_item_ids) - len(item_detail_errors),
                "list_errors": item_errors,
                "detail_errors": item_detail_errors,
            },
            "monsters": {
                "listed": len(monsters_list),
                "detail_pages_attempted": len(sample_monster_ids),
                "detail_pages_ok": len(sample_monster_ids) - len(monster_detail_errors),
                "list_errors": monster_errors,
                "detail_errors": monster_detail_errors,
            },
            "partners": {"listed": len(partners_list)},
            "pets": {"listed": len(pets_list)},
            "maps": {
                "listed": len(maps_list),
                "detail_pages_ok": len(maps_detail),
                "list_errors": map_errors,
            },
            "skills": {
                "categories_scraped": len(skill_categories),
                "unique_skills_listed": len(skills_by_id),
                "list_errors": skill_errors,
            },
            "quests": {
                "categories_in_tree": len(quest_category_defs),
                "categories_downloaded": len(quests_data),
                "total_quests_extracted": sum(len(v["quests"]) for v in quests_data.values()),
                "errors": quest_errors,
            },
        }
    )
    write_json(args.out_dir, "run_report.json", report.__dict__)

    print("\n=== RIEPILOGO ===")
    print(f"Richieste HTTP totali: {fetcher.request_count}")
    print(f"Item unici in elenco: {len(items_by_id)}  |  dettagli scaricati: {len(sample_item_ids) - len(item_detail_errors)}")
    print(f"Mostri in elenco: {len(monsters_list)}  |  dettagli scaricati: {len(sample_monster_ids) - len(monster_detail_errors)}")
    print(f"Compagni: {len(partners_list)}  |  Pet: {len(pets_list)}")
    print(f"Mappe in elenco: {len(maps_list)}  |  dettagli scaricati: {len(maps_detail)}")
    print(f"Skill uniche in elenco: {len(skills_by_id)}")
    print(f"Categorie quest scaricate: {len(quests_data)}  |  quest totali estratte: {sum(len(v['quests']) for v in quests_data.values())}")
    if fetcher.errors:
        print(f"ATTENZIONE: {len(fetcher.errors)} richieste HTTP fallite (vedi run_report.json -> global_errors)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
