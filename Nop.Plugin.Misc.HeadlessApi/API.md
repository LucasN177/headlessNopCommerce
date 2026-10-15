# Headless API – Dokumentation für das Frontend

Stand: 15.10.2026 · Plugin `Misc.HeadlessApi` · API-Version `v1`

Das Plugin stellt die Funktionen des nopCommerce-Storefronts als JSON-API bereit. Es verwendet intern dieselben Services und Model-Factories wie der Storefront. Preise, Rabatte, Steuern, Zugriffsrechte (ACL), Store-Zuordnung, Events und Aktivitätslog verhalten sich deshalb wie im normalen Shop.

> Die Beschreibungen der einzelnen Endpunkte stammen aus den Code-Kommentaren der Controller und sind daher englisch.

## Inhalt

- [Grundlagen](#grundlagen)
- [Anmeldung mit Tokens](#anmeldung-mit-tokens)
- [Fehlerformat](#fehlerformat)
- [Konventionen](#konventionen)
- [Typische Abläufe](#typische-abläufe)
- [Frontend-Seiten für E-Mail-Links und Weiterleitungen](#frontend-seiten-für-e-mail-links-und-weiterleitungen)
- [Einschränkungen](#einschränkungen)
- [Endpunkte](#endpunkte)
- [Anhang: Modelle des Plugins](#anhang-modelle-des-plugins)

## Grundlagen

- **Basis-URL:** `https://{shop}/api/headless/v1`. Alle Pfade in dieser Dokumentation sind relativ dazu, z. B. `GET /catalog/categories/5` → `GET https://{shop}/api/headless/v1/catalog/categories/5`.
- **Aktivierung:** Die API muss im Admin unter *Konfiguration → Lokale Plugins → Headless API* aktiviert sein. Ist sie deaktiviert, antworten alle Endpunkte mit `404`.
- **CORS:** Ruft das Frontend die API direkt aus dem Browser auf, muss seine Herkunft (z. B. `https://shop.example.com`) in der Plugin-Konfiguration unter *Allowed origins* eingetragen sein. Aufrufe von einem Server (SSR, BFF) brauchen das nicht.
- **Store:** Der Store wird wie im Storefront über den Hostnamen der Anfrage bestimmt.
- **Sprache, Währung, Steueranzeige:** Diese Werte gehören zum Kunden (auch zum Gast) und werden über `/context` gelesen und geändert. Es gibt keine Header oder Query-Parameter dafür.

## Anmeldung mit Tokens

Fast alle Endpunkte brauchen ein Token im Header:

```http
Authorization: Bearer {accessToken}
```

Tokens gibt es auch für Gäste. So hat das Frontend immer einen festen Kunden, dem Warenkorb, Wunschliste, Sprache, Währung und Checkout-Daten zugeordnet sind. Ohne Token sind nur `GET /status` und `POST /auth/guest` erreichbar.

1. **Erster Besuch:** `POST /auth/guest` liefert ein Gast-Token (`TokenModel`). Das Frontend speichert es, z. B. in einem Cookie seiner eigenen Domain, und sendet es bei jeder Anfrage mit.
2. **Login:** `POST /auth/login` mit dem Gast-Token im Header und `{ "email", "password" }` im Body. Sind im Shop Benutzernamen aktiviert, wird `{ "username", "password" }` verwendet. Der Warenkorb des Gasts wird in das Kundenkonto übernommen. Die Antwort enthält ein **neues Token**, das das Gast-Token ersetzt.
3. **Registrierung:** `POST /account/register` registriert den aktuellen Gast; Warenkorb und Wunschliste bleiben erhalten. Bei der Registrierungsart „Standard“ ist der Kunde sofort angemeldet, und das neue Token steht in `token` der Antwort. Bei E-Mail-Bestätigung oder Freigabe durch den Admin ist `token` leer.
4. **Erneuern:** `POST /auth/refresh` stellt ein neues Token aus, bevor das alte abläuft. Die Gültigkeit ist in der Plugin-Konfiguration einstellbar, Standard sind 30 Tage. Ablaufzeitpunkt: `expiresOnUtc`.
5. **Logout:** `POST /auth/logout` macht **alle** Tokens des Kunden ungültig, auch auf anderen Geräten. Danach holt sich das Frontend ein neues Gast-Token.
6. **Passwort ändern oder zurücksetzen:** Dabei werden die anderen Tokens des Kunden ungültig. Die Antwort enthält ein neues Token für das aktuelle Gerät.

Antwortet die API mit `401`, ist das Token abgelaufen oder ungültig, z. B. nach einem Logout auf einem anderen Gerät oder nach einem neu erzeugten Signaturschlüssel. Das Frontend holt sich dann ein neues Gast-Token und lässt den Kunden sich ggf. neu anmelden.

Bei manchen Endpunkten steht unten **„Token eines registrierten Kunden“**. Mit einem Gast-Token antworten diese mit `403`.

## Fehlerformat

Fehler werden als [Problem Details (RFC 7807)](https://www.rfc-editor.org/rfc/rfc7807) mit `Content-Type: application/problem+json` zurückgegeben.

| Status | Bedeutung |
|---|---|
| `400` | Eingaben ungültig oder von nopCommerce abgelehnt, z. B. Lagerbestand, Pflichtattribut, falscher Gutschein, Registrierungsfehler. Die Meldungen stehen in `errors`. |
| `401` | Kein, ein abgelaufenes oder ein widerrufenes Token. |
| `403` | Funktion nicht erlaubt: Gast statt registriertem Kunden, Funktion im Shop deaktiviert, kein Zugriff auf den Shop. Die Meldung steht in `detail`. |
| `404` | Nicht gefunden oder für den Kunden nicht verfügbar (unveröffentlicht, ACL, Store-Zuordnung). Auch alle Endpunkte, wenn die API deaktiviert ist. |
| `409` | Konflikt, z. B. Download erst nach Zustimmung zur Nutzungsvereinbarung. |
| `503` | Der Shop ist geschlossen; `detail` enthält den Hinweistext. |
| `500` | Unerwarteter Fehler. Details stehen nur im nopCommerce-Log. |

Beispiel für einen `400`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "": [ "Die Menge übersteigt den verfügbaren Lagerbestand." ],
    "newAddress.Email": [ "E-Mail ist erforderlich." ]
  }
}
```

Meldungen ohne zugehöriges Feld stehen unter dem Schlüssel `""`. Alle Texte sind in der aktuellen Sprache des Kunden übersetzt.

## Konventionen

- **JSON:**
  - Antworten verwenden camelCase-Feldnamen.
  - In Anfragen ist die Groß-/Kleinschreibung der Feldnamen egal.
  - Datumswerte sind ISO 8601, Zeitpunkte in UTC.
- **Modelle:** Die meisten Antworten sind die Storefront-Modelle von nopCommerce (`Nop.Web.Models.*`, z. B. `ProductDetailsModel`, `ShoppingCartModel`). Ihre Felder sind unter `src/Presentation/Nop.Web/Models` dokumentiert. Eigene Modelle des Plugins stehen im [Anhang](#anhang-modelle-des-plugins).
- **Ändernde Aufrufe geben den neuen Stand zurück**, z. B. liefert `POST /cart/items` den aktualisierten Warenkorb. Ein erneuter `GET` ist nicht nötig.
- **Formularfelder als Wörterbuch:** Wo nopCommerce dynamische Formularfelder hat, schickt das Frontend ein Feld `attributes` (bzw. `values`) als Objekt mit **denselben Schlüsseln wie das Storefront-Formular**:

  | Wofür | Schlüssel | Wert |
  |---|---|---|
  | Produktattribute | `product_attribute_{mappingId}` | Wert-ID; bei Checkboxen kommagetrennt (`"12,13"`); Text bei Textfeldern; Download-GUID bei Datei-Upload |
  | Menge pro Attributwert (falls aktiviert) | `product_attribute_{mappingId}_{valueId}_qty` | Zahl |
  | Datumsattribute | `{schlüssel}_day`, `{schlüssel}_month`, `{schlüssel}_year` | Zahl |
  | Geschenkkarten | `giftcard_{productId}.RecipientName`, `.RecipientEmail`, `.SenderName`, `.SenderEmail`, `.Message` | Text |
  | Bestellattribute (Warenkorb) | `checkout_attribute_{attributeId}` | wie Produktattribute |
  | Kundenattribute (Registrierung, Profil) | `customer_attribute_{attributeId}` | wie Produktattribute |
  | DSGVO-Einwilligungen (Registrierung, Profil) | `consent{consentId}` | `"on"` (oder `"true"`) = zugestimmt |
  | Adressattribute (Checkout) | `address_attribute_{attributeId}` | wie Produktattribute |
  | Zahlungsdaten (Checkout) | Feldnamen des Zahlungsplugins, z. B. `CardholderName`, `CardNumber`, `ExpireMonth`, `ExpireYear`, `CardCode` | Text |

  Die IDs liefern die jeweiligen Modelle, z. B. `ProductDetailsModel.productAttributes[].id`.
- **Datei-Uploads** werden als `multipart/form-data` gesendet. Die Antwort enthält eine Download-GUID, die das Frontend danach als Wert des Attributs mitschickt.
- **Datei-Downloads** (Rechnungs-PDF, Produkt-Downloads, DSGVO-Export) kommen als Datei zurück. Liegt ein Download nur als externe URL vor, antwortet die API mit `{ "downloadUrl": "…" }`.
- **Produktlisten** (Kategorie, Hersteller, Händler, Tag, Suche, neue Produkte) verstehen dieselben Query-Parameter wie der Storefront:
  - `pageNumber`, `pageSize`
  - `orderBy`: Sortier-ID aus `catalogProductsModel.availableSortOptions`
  - `viewMode`: `grid` oder `list`
  - `price`: z. B. `10-50`
  - `specs`: IDs von Spezifikationsoptionen, mehrfach möglich: `specs=1&specs=7`
  - `ms`: Hersteller-IDs

## Typische Abläufe

### SEO-URLs auflösen

Das Frontend kann dieselben URLs wie der Storefront verwenden, z. B. `/apple-iphone-16`.

1. `GET /common/url/resolve?slug=apple-iphone-16` liefert `entityName` und `entityId`, z. B. `Product` und `42`.
2. Ist `redirectToSlug` gesetzt, ist die URL veraltet. Das Frontend leitet dann mit `301` auf den neuen Slug weiter.
3. Je nach `entityName` lädt das Frontend die Daten:

   | `entityName` | Endpunkt |
   |---|---|
   | `Product` | `GET /products/{id}` |
   | `Category` | `GET /catalog/categories/{id}` |
   | `Manufacturer` | `GET /catalog/manufacturers/{id}` |
   | `Vendor` | `GET /catalog/vendors/{id}` |
   | `Topic` | `GET /common/topics/{id}` |
   | `BlogPost` | `GET /blog/{id}` |

### Layout

`GET /common/layout` liefert in einem Aufruf alles für Header und Footer:
- Logo, Header-Links, Footer, Social-Links, Favicons
- Sprach-, Währungs- und Steuerauswahl
- Einstellungen des Suchfelds
- Hinweise wie Shop geschlossen und Cookie-Hinweis

Menüs liefert `GET /common/menus/Main` bzw. `/common/menus/Footer`.

### Produktseite

1. `GET /products/{id}` liefert die Details inklusive Attributen, Bildern, Preisen und Spezifikationen.
2. Wählt der Kunde Attribute aus, liefert `POST /products/{id}/attributes/change` Preis, SKU, Lagerbestand, Bilder und die aktiven bzw. inaktiven Attribute.
3. `POST /cart/items` legt das Produkt in den Warenkorb, `POST /wishlist/items` auf die Wunschliste:

   ```json
   { "productId": 42, "quantity": 1, "attributes": { "product_attribute_5": "12" } }
   ```

### Checkout

Der Checkout entspricht dem One-Page-Checkout des Storefronts. Jeder Schritt antwortet mit `CheckoutStepModel`:
- `nextStep`: nächster Schritt
- `model`: Daten dieses Schritts
- `warnings`: Hinweise

Nicht benötigte Schritte werden automatisch übersprungen:
- Versand, wenn er nicht nötig ist
- Versand- bzw. Zahlungsart, wenn nur eine verfügbar ist
- Zahlungsdaten, wenn die Zahlungsart kein Formular hat

| `nextStep` | Endpunkt zum Speichern |
|---|---|
| `shipping_address` | `PUT /checkout/shipping-address` |
| `shipping_method` | `PUT /checkout/shipping-method` |
| `payment_method` | `PUT /checkout/payment-method` |
| `payment_info` | `PUT /checkout/payment-info` |
| `confirm` | `POST /checkout/confirm` |

Ablauf:

1. `POST /checkout/start` prüft den Warenkorb, setzt die Checkout-Daten zurück und liefert das Formular der Rechnungsadresse.
2. `PUT /checkout/billing-address`: entweder `{ "addressId": 3 }` oder `{ "newAddress": { … }, "shipToSameAddress": true }`.
3. Weitere Schritte laut `nextStep`.
4. `POST /checkout/confirm` platziert die Bestellung und liefert `PlaceOrderResultModel`:
   - Ist `paymentRedirectUrl` gesetzt (z. B. PayPal), leitet das Frontend den Kunden dorthin. Nach der Zahlung schickt der Anbieter ihn zum Storefront zurück, der ihn auf die Frontend-Seite „Bestellung abgeschlossen“ weiterleitet (siehe nächster Abschnitt).
   - Sonst zeigt das Frontend direkt die Abschlussseite: `GET /checkout/completed/{orderId}`.
5. Wurde die Zahlung abgebrochen, startet `POST /checkout/orders/{orderId}/repost-payment` sie erneut.

Weitere Checkout-Funktionen:
- Bestellattribute: `PUT /cart/checkout-attributes`, vor dem Start des Checkouts
- Versandkosten schätzen: `POST /cart/estimate-shipping`
- Gutscheine und Geschenkkarten: `/cart/discount-coupons`, `/cart/gift-cards`

## Frontend-Seiten für E-Mail-Links und Weiterleitungen

In der Plugin-Konfiguration (Bereich *Frontend*) werden die **Frontend-URL** und **Pfad-Vorlagen** eingetragen. Sind sie gesetzt, gilt:
- Links in den nopCommerce-E-Mails zeigen auf das Frontend.
- Die Rücksprünge von Zahlungsanbietern landen im Frontend.

Platzhalter in `{…}` werden ersetzt. Das Frontend braucht dafür folgende Seiten:

| Vorlage (Standardwert) | Platzhalter | Die Frontend-Seite ruft auf |
|---|---|---|
| Passwort zurücksetzen (`/password-recovery?token={token}&guid={guid}`) | `token`, `guid`, `email` | `POST /account/password-recovery/check`, dann `POST /account/password-recovery/confirm` |
| Kontoaktivierung (`/account/activation?token={token}&guid={guid}`) | `token`, `guid`, `email` | `POST /account/activation` |
| E-Mail-Bestätigung (`/account/email-revalidation?token={token}&guid={guid}`) | `token`, `guid`, `email` | `POST /account/email-revalidation` |
| Newsletter-Bestätigung (`/newsletter/activation?token={token}&active={active}`) | `token`, `active` | `POST /newsletter/activation` |
| Bestelldetails (`/account/orders/{orderId}`) | `orderId` | `GET /account/orders/{orderId}` |
| Sendungsdetails (`/account/shipments/{shipmentId}`) | `shipmentId` | `GET /account/shipments/{shipmentId}` |
| Produktseite (`/{seName}`) | `seName`, `productId` | `GET /common/url/resolve` bzw. `GET /products/{id}` |
| Bestellung abgeschlossen (`/checkout/completed/{orderId}`) | `orderId` | `GET /checkout/completed/{orderId}` |
| Warenkorb (`/cart`) | – | `GET /cart` |

Diese Links zeigen noch auf den Storefront, weil es dafür noch keine Vorlage gibt:
- geteilte Wunschliste
- Widerruf bzw. Retoure für Gäste
- Benachrichtigung über private Nachrichten
- Download- und Lizenz-Links in Bestellmails

## Einschränkungen

- **Kein CAPTCHA** bei Registrierung, Passwort vergessen, Kontaktformularen, Bewertungen, Blog-Kommentaren und Bestellungen als Gast. Als Ersatz empfiehlt sich ein Rate-Limit vor der API.
- **Zwei-Faktor-Anmeldung** wird nicht unterstützt. Der Login antwortet dann mit `403`.
- **SMS-Bestätigung der Telefonnummer** wird nicht unterstützt.
- **Zahlungsarten, die ein Formular an den Anbieter senden** (POST statt Weiterleitung), funktionieren beim Bestellen über die API nicht.
- **Keine Admin-Vorschau** unveröffentlichter Produkte, Kategorien usw.
- **Zuletzt angesehene Produkte und Produktvergleich** werden für API-Kunden am Kunden gespeichert statt im Cookie. Beim Login werden sie nicht übernommen.
- **Storefront-URLs in Antworten:** Einzelne Antworten enthalten URLs des Storefronts, z. B. Einträge der Sitemap oder die `seName`-Werte, aus denen das Frontend eigene URLs bildet.
- **News, Foren und Umfragen** sind eigene Plugins und haben noch keine Endpunkte.

## Endpunkte

- [Status](#status) (1)
- [Anmeldung und Tokens](#anmeldung-und-tokens) (4)
- [Kontext (Sprache, Währung, Steueranzeige)](#kontext-sprache-währung-steueranzeige) (4)
- [Allgemein (Layout, URL-Auflösung, Menüs, Inhaltsseiten, Sitemap)](#allgemein-layout-url-auflösung-menüs-inhaltsseiten-sitemap) (7)
- [Länder und Bundesländer](#länder-und-bundesländer) (2)
- [Katalog](#katalog) (23)
- [Produkte](#produkte) (12)
- [Produktbewertungen](#produktbewertungen) (3)
- [Produktvergleich](#produktvergleich) (4)
- [Benachrichtigung bei Verfügbarkeit](#benachrichtigung-bei-verfügbarkeit) (4)
- [Warenkorb](#warenkorb) (17)
- [Wunschlisten](#wunschlisten) (14)
- [Checkout](#checkout) (15)
- [Kundenkonto](#kundenkonto) (18)
- [Kundenkonto: Avatar, DSGVO, Geschenkkarten, Downloads](#kundenkonto-avatar-dsgvo-geschenkkarten-downloads) (9)
- [Bestellungen, Abos, Bonuspunkte, Retouren](#bestellungen-abos-bonuspunkte-retouren) (11)
- [Retouren (Upload, Gast-Widerruf)](#retouren-upload-gast-widerruf) (3)
- [Downloads](#downloads) (5)
- [Newsletter](#newsletter) (2)
- [Händlerkonto](#händlerkonto) (5)
- [Private Nachrichten](#private-nachrichten) (6)
- [Öffentliche Kundenprofile](#öffentliche-kundenprofile) (1)
- [Kontaktformulare](#kontaktformulare) (4)
- [Blog](#blog) (5)

### Status

Represents the status endpoint, which allows a frontend to check the API availability

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/status` | Gets the API status |

#### GET `/status`

Gets the API status

> The endpoint doesn't use the work context, so no guest customer is created for anonymous calls

- **Anmeldung:** ohne Token
- **Antworten:** 200 `StatusModel`

### Anmeldung und Tokens

Represents the endpoints to get API tokens and to log in and out

A frontend first requests a guest token and sends it with every request (Authorization: Bearer {token}). On login the guest's shopping cart is migrated to the registered customer and a new token is issued.

| Methode | Pfad | Beschreibung |
|---|---|---|
| POST | `/auth/guest` | Creates a new guest customer and issues a token for it |
| POST | `/auth/refresh` | Issues a new token for the current customer, e.g. before the current one expires |
| POST | `/auth/login` | Logs in a customer; the cart of the current (guest) customer is migrated to the logged in customer |
| POST | `/auth/logout` | Logs out the current customer and revokes all its tokens |

#### POST `/auth/guest`

Creates a new guest customer and issues a token for it

- **Anmeldung:** ohne Token
- **Antworten:** 200 `TokenModel`

#### POST `/auth/refresh`

Issues a new token for the current customer, e.g. before the current one expires

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Antworten:** 200 `TokenModel`, 401

#### POST `/auth/login`

Logs in a customer; the cart of the current (guest) customer is migrated to the logged in customer

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Body:** `LoginRequest`: Login request
- **Antworten:** 200 `TokenModel`, 400, 401, 403

#### POST `/auth/logout`

Logs out the current customer and revokes all its tokens

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Antworten:** 204, 401

### Kontext (Sprache, Währung, Steueranzeige)

Represents the endpoints to get and change the context of the current customer (language, currency, tax display type)

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/context` | Gets the context of the current customer |
| PUT | `/context/language` | Changes the working language of the current customer |
| PUT | `/context/currency` | Changes the working currency of the current customer |
| PUT | `/context/tax-type` | Changes the tax display type (prices including or excluding tax) of the current customer |

#### GET `/context`

Gets the context of the current customer

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `ContextModel`

#### PUT `/context/language`

Changes the working language of the current customer

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `SetLanguageRequest`: Request
- **Antworten:** 200 `ContextModel`, 400

#### PUT `/context/currency`

Changes the working currency of the current customer

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `SetCurrencyRequest`: Request
- **Antworten:** 200 `ContextModel`, 400

#### PUT `/context/tax-type`

Changes the tax display type (prices including or excluding tax) of the current customer

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `SetTaxTypeRequest`: Request
- **Antworten:** 200 `ContextModel`, 400, 403

### Allgemein (Layout, URL-Auflösung, Menüs, Inhaltsseiten, Sitemap)

Represents the common endpoints (layout, URL resolving, menus, topics, sitemap)

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/common/url/resolve` | Resolves an SEO friendly URL (slug) to the entity it belongs to |
| GET | `/common/menus/{menuType}` | Gets the menus of the passed type |
| GET | `/common/topics/{topicId}` | Gets a topic (content page) by identifier |
| GET | `/common/topics/{systemName}` | Gets a topic (content page) by system name, e.g. "ShippingInfo", "PrivacyInfo", "ConditionsOfUse" |
| GET | `/common/layout` | Gets the data of the common layout (logo, header links, footer, social links, favicon, selectors, search box, store state) |
| POST | `/common/eu-cookie-law/accept` | Stores that the current customer has accepted the EU cookie law warning |
| GET | `/common/sitemap` | Gets the sitemap page (general pages, categories, manufacturers, products, topics, ...); the item URLs are storefront URLs |

#### GET `/common/url/resolve`

Resolves an SEO friendly URL (slug) to the entity it belongs to

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `slug` (Query, `string`): Slug, e.g. "apple-iphone-16"
- **Antworten:** 200 `UrlResolveModel`, 404

#### GET `/common/menus/{menuType}`

Gets the menus of the passed type

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `menuType` (Pfad, `MenuType`): Menu type (Main, Footer)
- **Antworten:** 200 `IList<MenuModel>`

#### GET `/common/topics/{topicId}`

Gets a topic (content page) by identifier

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `topicId` (Pfad, `int`): Topic identifier
- **Antworten:** 200 `TopicModel`, 404

#### GET `/common/topics/{systemName}`

Gets a topic (content page) by system name, e.g. "ShippingInfo", "PrivacyInfo", "ConditionsOfUse"

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `systemName` (Pfad, `string`): Topic system name
- **Antworten:** 200 `TopicModel`, 404

#### GET `/common/layout`

Gets the data of the common layout (logo, header links, footer, social links, favicon, selectors, search box, store state)

> Available even when the store is closed or navigation is not allowed, so the frontend can render the layout of the login page

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Antworten:** 200 `LayoutModel`

#### POST `/common/eu-cookie-law/accept`

Stores that the current customer has accepted the EU cookie law warning

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Antworten:** 204, 403

#### GET `/common/sitemap`

Gets the sitemap page (general pages, categories, manufacturers, products, topics, ...); the item URLs are storefront URLs

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `pageModel` (Query, `SitemapPageModel`): Paging (PageNumber, starting with 1)
- **Antworten:** 200 `SitemapModel`, 403

### Länder und Bundesländer

Represents the directory endpoints (countries, states/provinces) for the address forms

Available even when the store is closed or navigation is not allowed, since they are needed by the registration form

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/countries` | Gets the published countries of the current store |
| GET | `/countries/{countryId}/states` | Gets the states/provinces of a country (like GetStatesByCountryId of the storefront) |

#### GET `/countries`

Gets the published countries of the current store

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Parameter:**
  - `type` (Query, `CountryAddressType`): Address type: All, Billing (countries allowing billing) or Shipping (countries allowing shipping)
- **Antworten:** 200 `IList<CountryModel>`

#### GET `/countries/{countryId}/states`

Gets the states/provinces of a country (like GetStatesByCountryId of the storefront)

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Parameter:**
  - `countryId` (Pfad, `int`): Country identifier
  - `addSelectStateItem` (Query, `bool`): Whether to add the "Select state" item (id 0) if the country has states;
    /// if the country has no states, the result always contains the "Other" item (id 0)
- **Antworten:** 200 `IList<StateProvinceModel>`

### Katalog

Represents the catalog endpoints (categories, manufacturers, vendors, product tags, search)

Product lists support the same query parameters as the storefront (CatalogProductsCommand): pageNumber, pageSize, orderBy, viewMode, price (e.g. "10-50"), specs (specification attribute option ids), ms (manufacturer ids)

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/catalog/homepage/categories` | Gets the categories displayed on the homepage |
| GET | `/catalog/homepage/products` | Gets the products displayed on the homepage (featured products) |
| GET | `/catalog/categories/{categoryId}` | Gets a category including its first page of products, subcategories and filters |
| GET | `/catalog/categories/{categoryId}/products` | Gets the products of a category (e.g. for paging or filtering without reloading the category) |
| GET | `/catalog/categories/navigation` | Gets the category navigation (category tree of the side column) |
| GET | `/catalog/manufacturers` | Gets all manufacturers |
| GET | `/catalog/manufacturers/{manufacturerId}` | Gets a manufacturer including its first page of products and filters |
| GET | `/catalog/manufacturers/{manufacturerId}/products` | Gets the products of a manufacturer |
| GET | `/catalog/manufacturers/navigation` | Gets the manufacturer navigation (manufacturers block of the side column) |
| GET | `/catalog/vendors/{vendorId}` | Gets a vendor including its first page of products |
| GET | `/catalog/vendors/{vendorId}/products` | Gets the products of a vendor |
| GET | `/catalog/vendors/{vendorId}/reviews` | Gets the reviews of the products of a vendor |
| GET | `/catalog/vendors` | Gets all vendors |
| GET | `/catalog/vendors/navigation` | Gets the vendor navigation (vendors block of the side column) |
| GET | `/catalog/tags` | Gets the popular product tags |
| GET | `/catalog/tags/{productTagId}` | Gets the products tagged with a product tag |
| GET | `/catalog/new-products` | Gets the recently added products |
| GET | `/catalog/search` | Searches products |
| GET | `/catalog/search/autocomplete` | Gets the products matching a search term (e.g. for a search box with suggestions) |
| GET | `/catalog/filter-levels/values` | Gets the filter level values for a cascading selection (e.g. make, then model, then year) |
| GET | `/catalog/filter-levels/search-form` | Gets the filter level search form (e.g. the "find parts for your vehicle" block of the home page) |
| GET | `/catalog/filter-levels/search` | Searches products by filter level values, including the available values and the first page of products |
| GET | `/catalog/filter-levels/search/products` | Gets the products matching the filter level values (e.g. for paging without reloading the search) |

#### GET `/catalog/homepage/categories`

Gets the categories displayed on the homepage

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `List<CategoryModel>`

#### GET `/catalog/homepage/products`

Gets the products displayed on the homepage (featured products)

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `List<ProductOverviewModel>`

#### GET `/catalog/categories/{categoryId}`

Gets a category including its first page of products, subcategories and filters

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `categoryId` (Pfad, `int`): Category identifier
  - `command` (Query, `CatalogProductsCommand`): Paging, sorting and filtering
- **Antworten:** 200 `CategoryModel`, 404

#### GET `/catalog/categories/{categoryId}/products`

Gets the products of a category (e.g. for paging or filtering without reloading the category)

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `categoryId` (Pfad, `int`): Category identifier
  - `command` (Query, `CatalogProductsCommand`): Paging, sorting and filtering
- **Antworten:** 200 `CatalogProductsModel`, 404

#### GET `/catalog/categories/navigation`

Gets the category navigation (category tree of the side column)

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `currentCategoryId` (Query, `int`): Identifier of the currently displayed category; 0 if no category is displayed
  - `currentProductId` (Query, `int`): Identifier of the currently displayed product (its category is highlighted); 0 if no product is displayed
- **Antworten:** 200 `CategoryNavigationModel`

#### GET `/catalog/manufacturers`

Gets all manufacturers

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `List<ManufacturerModel>`

#### GET `/catalog/manufacturers/{manufacturerId}`

Gets a manufacturer including its first page of products and filters

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `manufacturerId` (Pfad, `int`): Manufacturer identifier
  - `command` (Query, `CatalogProductsCommand`): Paging, sorting and filtering
- **Antworten:** 200 `ManufacturerModel`, 404

#### GET `/catalog/manufacturers/{manufacturerId}/products`

Gets the products of a manufacturer

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `manufacturerId` (Pfad, `int`): Manufacturer identifier
  - `command` (Query, `CatalogProductsCommand`): Paging, sorting and filtering
- **Antworten:** 200 `CatalogProductsModel`, 404

#### GET `/catalog/manufacturers/navigation`

Gets the manufacturer navigation (manufacturers block of the side column)

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `currentManufacturerId` (Query, `int`): Identifier of the currently displayed manufacturer; 0 if no manufacturer is displayed
- **Antworten:** 200 `ManufacturerNavigationModel`, 404

#### GET `/catalog/vendors/{vendorId}`

Gets a vendor including its first page of products

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `vendorId` (Pfad, `int`): Vendor identifier
  - `command` (Query, `CatalogProductsCommand`): Paging, sorting and filtering
- **Antworten:** 200 `VendorModel`, 404

#### GET `/catalog/vendors/{vendorId}/products`

Gets the products of a vendor

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `vendorId` (Pfad, `int`): Vendor identifier
  - `command` (Query, `CatalogProductsCommand`): Paging, sorting and filtering
- **Antworten:** 200 `CatalogProductsModel`, 404

#### GET `/catalog/vendors/{vendorId}/reviews`

Gets the reviews of the products of a vendor

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `vendorId` (Pfad, `int`): Vendor identifier
  - `pageNumber` (Query, `int`): Page number (starting from 1)
- **Antworten:** 200 `VendorProductReviewsListModel`, 404

#### GET `/catalog/vendors`

Gets all vendors

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `List<VendorModel>`, 404

#### GET `/catalog/vendors/navigation`

Gets the vendor navigation (vendors block of the side column)

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `VendorNavigationModel`, 404

#### GET `/catalog/tags`

Gets the popular product tags

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `PopularProductTagsModel`

#### GET `/catalog/tags/{productTagId}`

Gets the products tagged with a product tag

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productTagId` (Pfad, `int`): Product tag identifier
  - `command` (Query, `CatalogProductsCommand`): Paging, sorting and filtering
- **Antworten:** 200 `ProductsByTagModel`, 404

#### GET `/catalog/new-products`

Gets the recently added products

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `command` (Query, `CatalogProductsCommand`): Paging, sorting and filtering
- **Antworten:** 200 `CatalogProductsModel`, 404

#### GET `/catalog/search`

Searches products

> Query parameters as in the storefront: q (keywords), advs (advanced search), cid (category), isc (include subcategories), mid (manufacturer), vid (vendor), sid (search in descriptions), plus paging, sorting and filtering

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `model` (Query, `SearchModel`): Search parameters
  - `command` (Query, `CatalogProductsCommand`): Paging, sorting and filtering
- **Antworten:** 200 `SearchModel`

#### GET `/catalog/search/autocomplete`

Gets the products matching a search term (e.g. for a search box with suggestions)

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `term` (Query, `string`): Search term
  - `categoryId` (Query, `int`): Category identifier to search in (including subcategories); 0 to search in all categories
- **Antworten:** 200 `List<ProductOverviewModel>`

#### GET `/catalog/filter-levels/values`

Gets the filter level values for a cascading selection (e.g. make, then model, then year)

> Without parameters the level 1 values are returned; pass the selected level 1 value to get the level 2 values and so on

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `filterLevel1Value` (Query, `string`): Selected level 1 value
  - `filterLevel2Value` (Query, `string`): Selected level 2 value
  - `filterLevel3Value` (Query, `string`): Selected level 3 value
- **Antworten:** 200 `FilterLevelValuesModel`, 404

#### GET `/catalog/filter-levels/search-form`

Gets the filter level search form (e.g. the "find parts for your vehicle" block of the home page)

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `FilterLevelValueSearchModel`, 404

#### GET `/catalog/filter-levels/search`

Searches products by filter level values, including the available values and the first page of products

> Query parameters as in the storefront: fl1id, fl2id, fl3id (selected filter level values), plus paging, sorting and filtering

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `model` (Query, `SearchFilterLevelValueModel`): Search parameters
  - `command` (Query, `CatalogProductsCommand`): Paging, sorting and filtering
- **Antworten:** 200 `SearchFilterLevelValueModel`, 404

#### GET `/catalog/filter-levels/search/products`

Gets the products matching the filter level values (e.g. for paging without reloading the search)

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `model` (Query, `SearchFilterLevelValueModel`): Search parameters (fl1id, fl2id, fl3id)
  - `command` (Query, `CatalogProductsCommand`): Paging, sorting and filtering
- **Antworten:** 200 `CatalogProductsModel`, 404

### Produkte

Represents the product endpoints

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/products/{productId}` | Gets the product details |
| POST | `/products/{productId}/attributes/change` | Recalculates the product details (price, sku/mpn/gtin, stock availability, conditional attributes, pictures) after the customer changed the selected attributes |
| GET | `/products/{productId}/combinations` | Gets the attribute combinations of the product (e.g. to disable out of stock combinations) |
| POST | `/products/{productId}/estimate-shipping` | Estimates the shipping of the product with the selected attributes |
| POST | `/products/{productId}/attributes/{attributeId}/upload` | Uploads a file for a "file upload" product attribute |
| GET | `/products/{productId}/filter-level-values` | Gets the filter level values (e.g. make/model/year) the product is compatible with |
| GET | `/products/{productId}/reviews` | Gets the product reviews |
| GET | `/products/{productId}/related` | Gets the related products |
| GET | `/products/{productId}/also-purchased` | Gets the products that customers who bought this product also bought |
| GET | `/products/recently-viewed` | Gets the products recently viewed by the current customer |
| GET | `/products/{productId}/email-a-friend` | Gets the "email a friend" form of the product (e.g. with the prefilled email address of the customer) |
| POST | `/products/{productId}/email-a-friend` | Sends the product to a friend by email |

#### GET `/products/{productId}`

Gets the product details

> Associated products of a grouped product are not visible individually, they are included in the details of the grouped product. The product is added to the recently viewed products of the current customer.

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Antworten:** 200 `ProductDetailsModel`, 404

#### POST `/products/{productId}/attributes/change`

Recalculates the product details (price, sku/mpn/gtin, stock availability, conditional attributes, pictures) after the customer changed the selected attributes

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Body:** `ProductAttributeChangeRequest`: Request
- **Antworten:** 200 `ProductAttributeChangeModel`, 404

#### GET `/products/{productId}/combinations`

Gets the attribute combinations of the product (e.g. to disable out of stock combinations)

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Antworten:** 200 `IList<ProductCombinationModel>`, 404

#### POST `/products/{productId}/estimate-shipping`

Estimates the shipping of the product with the selected attributes

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Body:** `ProductEstimateShippingRequest`: Request
- **Antworten:** 200 `EstimateShippingResultModel`, 400, 404

#### POST `/products/{productId}/attributes/{attributeId}/upload`

Uploads a file for a "file upload" product attribute

> Send the file as multipart/form-data; the returned download GUID has to be passed as value of the attribute (e.g. "product_attribute_5": "{downloadGuid}") when adding the product to the cart

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
  - `attributeId` (Pfad, `int`): Product attribute mapping identifier
  - `file` (Form-Data (multipart), `IFormFile`): File
- **Antworten:** 200 `UploadedFileModel`, 400, 404

#### GET `/products/{productId}/filter-level-values`

Gets the filter level values (e.g. make/model/year) the product is compatible with

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Antworten:** 200 `FilterLevelValueOverviewModel`, 404

#### GET `/products/{productId}/reviews`

Gets the product reviews

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Antworten:** 200 `ProductReviewsModel`, 404

#### GET `/products/{productId}/related`

Gets the related products

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Antworten:** 200 `List<ProductOverviewModel>`, 404

#### GET `/products/{productId}/also-purchased`

Gets the products that customers who bought this product also bought

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
  - `productThumbPictureSize` (Query, `int?`): Product thumb picture size (longest side); null to use the default value of the media settings
- **Antworten:** 200 `List<ProductOverviewModel>`, 404

#### GET `/products/recently-viewed`

Gets the products recently viewed by the current customer

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productThumbPictureSize` (Query, `int?`): Product thumb picture size (longest side); null to use the default value of the media settings
  - `preparePriceModel` (Query, `bool`): Whether to prepare the prices
- **Antworten:** 200 `List<ProductOverviewModel>`, 404

#### GET `/products/{productId}/email-a-friend`

Gets the "email a friend" form of the product (e.g. with the prefilled email address of the customer)

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Antworten:** 200 `ProductEmailAFriendModel`, 404

#### POST `/products/{productId}/email-a-friend`

Sends the product to a friend by email

> CAPTCHA is not supported by the API, so the CAPTCHA setting "Show on email product to friend page" is ignored

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Body:** `ProductEmailAFriendModel`: Form model (friendEmail, yourEmailAddress, personalMessage)
- **Antworten:** 200 `ProductEmailAFriendModel`, 400, 404

### Produktbewertungen

Represents the product review endpoints (the reviews of a product are available via GET products/{productId}/reviews)

| Methode | Pfad | Beschreibung |
|---|---|---|
| POST | `/products/{productId}/reviews` | Adds a product review |
| POST | `/products/reviews/{reviewId}/helpfulness` | Votes for the helpfulness of a product review |
| GET | `/products/reviews/mine` | Gets the product reviews written by the current customer |

#### POST `/products/{productId}/reviews`

Adds a product review

> Request body: { "addProductReview": { "title": "...", "reviewText": "...", "rating": 5 }, "addAdditionalProductReviewList": [ { "reviewTypeId": 1, "rating": 4 } ] }. CAPTCHA is not supported by the API, so the CAPTCHA setting "Show on product reviews page" is ignored

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Body:** `ProductReviewsModel`: Product reviews model containing the review to add
- **Antworten:** 200 `AddProductReviewResultModel`, 400, 404

#### POST `/products/reviews/{reviewId}/helpfulness`

Votes for the helpfulness of a product review

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `reviewId` (Pfad, `int`): Product review identifier
- **Body:** `ReviewHelpfulnessRequest`: Request
- **Antworten:** 200 `ReviewHelpfulnessResultModel`, 403, 404

#### GET `/products/reviews/mine`

Gets the product reviews written by the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `page` (Query, `int?`): Page number (starting from 1)
- **Antworten:** 200 `CustomerProductReviewsModel`, 403, 404

### Produktvergleich

Represents the compare products endpoints

Unlike the storefront (cookie), the list is stored per customer (see HeadlessApiCompareProductsService). All endpoints return the updated list, so the frontend doesn't need to reload it

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/compare` | Gets the compared products of the current customer (including their specification attributes) |
| POST | `/compare/{productId}` | Adds a product to the compare list |
| DELETE | `/compare/{productId}` | Removes a product from the compare list |
| DELETE | `/compare` | Clears the compare list |

#### GET `/compare`

Gets the compared products of the current customer (including their specification attributes)

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `CompareProductsModel`, 404

#### POST `/compare/{productId}`

Adds a product to the compare list

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Antworten:** 200 `CompareProductsModel`, 404

#### DELETE `/compare/{productId}`

Removes a product from the compare list

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Antworten:** 200 `CompareProductsModel`, 404

#### DELETE `/compare`

Clears the compare list

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `CompareProductsModel`, 404

### Benachrichtigung bei Verfügbarkeit

Represents the back in stock subscription endpoints

The routes are defined on the actions, because they belong to the product (products/...) and to the customer account (account/...)

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/products/{productId}/back-in-stock-subscription` | Gets the back in stock subscription status of the current customer for the product |
| POST | `/products/{productId}/back-in-stock-subscription` | Subscribes the current customer to the back in stock notification of the product, or unsubscribes if already subscribed |
| GET | `/account/back-in-stock-subscriptions` | Gets the back in stock subscriptions of the current customer |
| DELETE | `/account/back-in-stock-subscriptions/{subscriptionId}` | Deletes a back in stock subscription of the current customer |

#### GET `/products/{productId}/back-in-stock-subscription`

Gets the back in stock subscription status of the current customer for the product

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Antworten:** 200 `BackInStockSubscribeModel`, 404

#### POST `/products/{productId}/back-in-stock-subscription`

Subscribes the current customer to the back in stock notification of the product, or unsubscribes if already subscribed

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Antworten:** 200 `BackInStockSubscriptionResultModel`, 400, 403, 404

#### GET `/account/back-in-stock-subscriptions`

Gets the back in stock subscriptions of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `page` (Query, `int?`): Page number (starting from 1)
- **Antworten:** 200 `CustomerBackInStockSubscriptionsModel`, 403, 404

#### DELETE `/account/back-in-stock-subscriptions/{subscriptionId}`

Deletes a back in stock subscription of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `subscriptionId` (Pfad, `int`): Subscription identifier
- **Antworten:** 200 `CustomerBackInStockSubscriptionsModel`, 403, 404

### Warenkorb

Represents the shopping cart endpoints

All changing endpoints return the updated cart, so the frontend doesn't need to reload it

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/cart` | Gets the shopping cart of the current customer |
| GET | `/cart/totals` | Gets the totals of the shopping cart (subtotal, shipping, tax, discounts, total) |
| POST | `/cart/items` | Adds a product to the shopping cart |
| PUT | `/cart/items/{itemId}` | Changes the quantity of a shopping cart item; the item is removed if the quantity is 0 |
| DELETE | `/cart/items/{itemId}` | Removes an item from the shopping cart |
| POST | `/cart/discount-coupons` | Applies a discount coupon code |
| DELETE | `/cart/discount-coupons/{discountId}` | Removes an applied discount coupon code |
| POST | `/cart/gift-cards` | Applies a gift card code |
| DELETE | `/cart/gift-cards/{giftCardId}` | Removes an applied gift card code |
| DELETE | `/cart` | Clears the shopping cart (items set aside by the vendor selection are moved back to the cart) |
| GET | `/cart/mini` | Gets the mini shopping cart (flyout cart) |
| PUT | `/cart/vendor` | Selects the vendor whose products are checked out; the items of other vendors are set aside until another vendor (or all vendors) is selected |
| GET | `/cart/estimate-shipping` | Gets the shipping estimation form (enabled flag, countries, states, default address of the customer) |
| POST | `/cart/estimate-shipping` | Estimates the shipping of the shopping cart; the offered options are cached, so one of them can be selected afterwards |
| POST | `/cart/select-shipping-option` | Selects a shipping option offered by the shipping estimation |
| PUT | `/cart/checkout-attributes` | Saves the checkout attributes of the shopping cart (the available attributes are part of the shopping cart model) |
| POST | `/cart/checkout-attributes/{attributeId}/upload` | Uploads a file for a checkout attribute of the "File upload" type (multipart/form-data, first file); the returned download GUID is then passed as the attribute value |

#### GET `/cart`

Gets the shopping cart of the current customer

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `ShoppingCartModel`, 403

#### GET `/cart/totals`

Gets the totals of the shopping cart (subtotal, shipping, tax, discounts, total)

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `OrderTotalsModel`, 403

#### POST `/cart/items`

Adds a product to the shopping cart

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `AddToCartRequest`: Request
- **Antworten:** 200 `ShoppingCartModel`, 400, 403

#### PUT `/cart/items/{itemId}`

Changes the quantity of a shopping cart item; the item is removed if the quantity is 0

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `itemId` (Pfad, `int`): Shopping cart item identifier
- **Body:** `UpdateCartItemRequest`: Request
- **Antworten:** 200 `ShoppingCartModel`, 400, 404

#### DELETE `/cart/items/{itemId}`

Removes an item from the shopping cart

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `itemId` (Pfad, `int`): Shopping cart item identifier
- **Antworten:** 200 `ShoppingCartModel`, 404

#### POST `/cart/discount-coupons`

Applies a discount coupon code

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `CouponCodeRequest`: Request
- **Antworten:** 200 `ShoppingCartModel`, 400

#### DELETE `/cart/discount-coupons/{discountId}`

Removes an applied discount coupon code

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `discountId` (Pfad, `int`): Discount identifier (see DiscountBox.AppliedDiscountsWithCodes of the cart)
- **Antworten:** 200 `ShoppingCartModel`

#### POST `/cart/gift-cards`

Applies a gift card code

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `CouponCodeRequest`: Request
- **Antworten:** 200 `ShoppingCartModel`, 400

#### DELETE `/cart/gift-cards/{giftCardId}`

Removes an applied gift card code

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `giftCardId` (Pfad, `int`): Gift card identifier (see the applied gift cards in the cart totals)
- **Antworten:** 200 `ShoppingCartModel`

#### DELETE `/cart`

Clears the shopping cart (items set aside by the vendor selection are moved back to the cart)

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `ShoppingCartModel`, 403

#### GET `/cart/mini`

Gets the mini shopping cart (flyout cart)

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `MiniShoppingCartModel`, 403

#### PUT `/cart/vendor`

Selects the vendor whose products are checked out; the items of other vendors are set aside until another vendor (or all vendors) is selected

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `SetCartVendorRequest`: Request
- **Antworten:** 200 `ShoppingCartModel`, 403

#### GET `/cart/estimate-shipping`

Gets the shipping estimation form (enabled flag, countries, states, default address of the customer)

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `EstimateShippingModel`, 403

#### POST `/cart/estimate-shipping`

Estimates the shipping of the shopping cart; the offered options are cached, so one of them can be selected afterwards

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `EstimateShippingRequest`: Request
- **Antworten:** 200 `EstimateShippingResultModel`, 400, 403

#### POST `/cart/select-shipping-option`

Selects a shipping option offered by the shipping estimation

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `SelectShippingOptionRequest`: Request
- **Antworten:** 200 `OrderTotalsModel`, 400, 403

#### PUT `/cart/checkout-attributes`

Saves the checkout attributes of the shopping cart (the available attributes are part of the shopping cart model)

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `CheckoutAttributesRequest`: Request
- **Antworten:** 200 `CheckoutAttributesResultModel`, 403

#### POST `/cart/checkout-attributes/{attributeId}/upload`

Uploads a file for a checkout attribute of the "File upload" type (multipart/form-data, first file); the returned download GUID is then passed as the attribute value

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `attributeId` (Pfad, `int`): Checkout attribute identifier
- **Antworten:** 200 `FileUploadResultModel`, 400, 403

### Wunschlisten

Represents the wishlist endpoints (default and custom wishlists, shared wishlists, email a friend)

All changing endpoints return the updated wishlist, so the frontend doesn't need to reload it. The available custom wishlists of the customer are part of the wishlist model ("customWishlistItems"). CAPTCHA is not supported by the API.

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/wishlist` | Gets the wishlist of the current customer |
| GET | `/wishlist/shared/{customerGuid}` | Gets the wishlist shared by another customer (read-only) |
| POST | `/wishlist/items` | Adds a product to the wishlist |
| PUT | `/wishlist/items/{itemId}` | Changes the quantity of a wishlist item; the item is removed if the quantity is 0 |
| DELETE | `/wishlist/items/{itemId}` | Removes an item from the wishlist |
| POST | `/wishlist/items/{itemId}/move-to-cart` | Moves a wishlist item to the shopping cart |
| POST | `/wishlist/items/{itemId}/move` | Moves a wishlist item to another wishlist of the current customer |
| POST | `/wishlist/add-to-cart` | Adds wishlist items to the shopping cart; items of the own wishlist are removed from it if configured ("Move items from wishlist to cart") |
| POST | `/wishlist/lists` | Creates a custom wishlist; optionally moves a product from the default wishlist to it |
| PUT | `/wishlist/lists/{listId}` | Renames a custom wishlist |
| DELETE | `/wishlist/lists/{listId}` | Deletes a custom wishlist including its items |
| POST | `/wishlist/lists/{listId}/products/{productId}` | Moves a product from the default wishlist to a custom wishlist (e.g. right after adding it to the wishlist) |
| GET | `/wishlist/email` | Gets the "Email a friend" form of a wishlist of the current customer |
| POST | `/wishlist/email` | Sends a link to a wishlist of the current customer to a friend |

#### GET `/wishlist`

Gets the wishlist of the current customer

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `listId` (Query, `int?`): Custom wishlist identifier; empty for the default wishlist
- **Antworten:** 200 `WishlistModel`, 403

#### GET `/wishlist/shared/{customerGuid}`

Gets the wishlist shared by another customer (read-only)

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `customerGuid` (Pfad, `Guid`): GUID of the customer who shared the wishlist
  - `listId` (Query, `int?`): Custom wishlist identifier; empty for the default wishlist
- **Antworten:** 200 `WishlistModel`, 403, 404

#### POST `/wishlist/items`

Adds a product to the wishlist

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `AddToCartRequest`: Request
- **Antworten:** 200 `WishlistModel`, 400, 403

#### PUT `/wishlist/items/{itemId}`

Changes the quantity of a wishlist item; the item is removed if the quantity is 0

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `itemId` (Pfad, `int`): Wishlist item identifier
- **Body:** `UpdateCartItemRequest`: Request
- **Antworten:** 200 `WishlistModel`, 400, 404

#### DELETE `/wishlist/items/{itemId}`

Removes an item from the wishlist

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `itemId` (Pfad, `int`): Wishlist item identifier
- **Antworten:** 200 `WishlistModel`, 404

#### POST `/wishlist/items/{itemId}/move-to-cart`

Moves a wishlist item to the shopping cart

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `itemId` (Pfad, `int`): Wishlist item identifier
- **Antworten:** 200 `WishlistModel`, 400, 404

#### POST `/wishlist/items/{itemId}/move`

Moves a wishlist item to another wishlist of the current customer

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `itemId` (Pfad, `int`): Wishlist item identifier
- **Body:** `MoveWishlistItemRequest`: Target wishlist
- **Antworten:** 200 `WishlistModel`, 403, 404

#### POST `/wishlist/add-to-cart`

Adds wishlist items to the shopping cart; items of the own wishlist are removed from it if configured ("Move items from wishlist to cart")

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `AddWishlistItemsToCartRequest`: Items to add and the wishlist they belong to (own or shared)
- **Antworten:** 200 `AddWishlistItemsToCartResultModel`, 400, 403, 404

#### POST `/wishlist/lists`

Creates a custom wishlist; optionally moves a product from the default wishlist to it

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `CustomWishlistRequest`: Wishlist name and optional product
- **Antworten:** 200 `WishlistModel`, 400, 403

#### PUT `/wishlist/lists/{listId}`

Renames a custom wishlist

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `listId` (Pfad, `int`): Custom wishlist identifier
- **Body:** `CustomWishlistRequest`: New name
- **Antworten:** 200 `WishlistModel`, 400, 404

#### DELETE `/wishlist/lists/{listId}`

Deletes a custom wishlist including its items

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `listId` (Pfad, `int`): Custom wishlist identifier
- **Antworten:** 200 `WishlistModel`, 404

#### POST `/wishlist/lists/{listId}/products/{productId}`

Moves a product from the default wishlist to a custom wishlist (e.g. right after adding it to the wishlist)

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `listId` (Pfad, `int`): Custom wishlist identifier
  - `productId` (Pfad, `int`): Product identifier
- **Antworten:** 200 `WishlistModel`, 403, 404

#### GET `/wishlist/email`

Gets the "Email a friend" form of a wishlist of the current customer

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `listId` (Query, `int?`): Custom wishlist identifier; empty for the default wishlist
- **Antworten:** 200 `WishlistEmailAFriendModel`, 403, 404

#### POST `/wishlist/email`

Sends a link to a wishlist of the current customer to a friend

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `WishlistEmailAFriendModel`: Friend's email, own email, personal message and the wishlist ("listId")
- **Antworten:** 200 `WishlistEmailAFriendModel`, 400, 403, 404

### Checkout

Represents the checkout endpoints

The flow corresponds to the one page checkout of the storefront: POST checkout/start → PUT billing-address → (PUT shipping-address) → (PUT shipping-method) → (PUT payment-method) → (PUT payment-info) → POST confirm. Each step returns the next step and its model; steps that are not required are skipped. The checkout data is stored at the customer, so the steps can be called in separate requests.

| Methode | Pfad | Beschreibung |
|---|---|---|
| POST | `/checkout/start` | Starts the checkout: validates the cart, resets the checkout data and returns the first step |
| GET | `/checkout/billing-address` | Gets the billing address step (existing addresses and the form of a new address) |
| PUT | `/checkout/billing-address` | Sets the billing address (an existing one or a new one) |
| GET | `/checkout/shipping-address` | Gets the shipping address step (existing addresses, the form of a new address and the pickup points) |
| PUT | `/checkout/shipping-address` | Sets the shipping address (an existing one, a new one or a pickup point) |
| GET | `/checkout/shipping-methods` | Gets the available shipping methods |
| PUT | `/checkout/shipping-method` | Selects the shipping method (or a pickup point) |
| GET | `/checkout/payment-methods` | Gets the available payment methods |
| PUT | `/checkout/payment-method` | Selects the payment method |
| GET | `/checkout/payment-info` | Gets the payment info step of the selected payment method |
| PUT | `/checkout/payment-info` | Enters the payment information; the values are validated by the payment plugin |
| GET | `/checkout/confirm` | Gets the confirm step (terms of service, minimum order totals, warnings) |
| POST | `/checkout/confirm` | Places the order |
| GET | `/checkout/completed/{orderId?}` | Gets the "checkout completed" data of an order |
| POST | `/checkout/orders/{orderId}/repost-payment` | Retries the payment of an order (e.g. after the customer canceled the payment at the payment provider) |

#### POST `/checkout/start`

Starts the checkout: validates the cart, resets the checkout data and returns the first step

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `OnePageCheckoutModel`, 400, 403

#### GET `/checkout/billing-address`

Gets the billing address step (existing addresses and the form of a new address)

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `CheckoutBillingAddressModel`

#### PUT `/checkout/billing-address`

Sets the billing address (an existing one or a new one)

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `CheckoutAddressRequest`: Request
- **Antworten:** 200 `CheckoutStepModel`, 400

#### GET `/checkout/shipping-address`

Gets the shipping address step (existing addresses, the form of a new address and the pickup points)

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `CheckoutShippingAddressModel`, 400

#### PUT `/checkout/shipping-address`

Sets the shipping address (an existing one, a new one or a pickup point)

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `CheckoutAddressRequest`: Request
- **Antworten:** 200 `CheckoutStepModel`, 400

#### GET `/checkout/shipping-methods`

Gets the available shipping methods

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `CheckoutShippingMethodModel`, 400

#### PUT `/checkout/shipping-method`

Selects the shipping method (or a pickup point)

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `ShippingMethodRequest`: Request
- **Antworten:** 200 `CheckoutStepModel`, 400

#### GET `/checkout/payment-methods`

Gets the available payment methods

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `CheckoutPaymentMethodModel`

#### PUT `/checkout/payment-method`

Selects the payment method

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `PaymentMethodRequest`: Request
- **Antworten:** 200 `CheckoutStepModel`, 400

#### GET `/checkout/payment-info`

Gets the payment info step of the selected payment method

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `PaymentInfoModel`, 400

#### PUT `/checkout/payment-info`

Enters the payment information; the values are validated by the payment plugin

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `PaymentInfoRequest`: Request
- **Antworten:** 200 `CheckoutStepModel`, 400

#### GET `/checkout/confirm`

Gets the confirm step (terms of service, minimum order totals, warnings)

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `CheckoutConfirmModel`

#### POST `/checkout/confirm`

Places the order

> For redirection payment methods (e.g. PayPal) the result contains the URL of the payment provider; the frontend has to redirect the customer there. CAPTCHA for guests is not supported by the API.

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `PlaceOrderResultModel`, 400

#### GET `/checkout/completed/{orderId?}`

Gets the "checkout completed" data of an order

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `orderId` (Pfad, `int?`): Order identifier; empty to get the last order of the customer
- **Antworten:** 200 `CheckoutCompletedModel`, 404

#### POST `/checkout/orders/{orderId}/repost-payment`

Retries the payment of an order (e.g. after the customer canceled the payment at the payment provider)

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `orderId` (Pfad, `int`): Order identifier
- **Antworten:** 200 `PlaceOrderResultModel`, 400, 404

### Kundenkonto

Represents the customer account endpoints (registration, account activation, password recovery, customer info, password, addresses, orders)

Request bodies use the storefront models of nopCommerce (RegisterModel, CustomerInfoModel, ChangePasswordModel, AddressModel), so they are validated by the same validators (required fields, password rules, etc.). Custom customer attributes and GDPR consents are passed in the "attributes" dictionary using the keys of the storefront form. CAPTCHA is not supported by the API.

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/account/register` | Gets the registration form (enabled and required fields, available countries, custom attributes, GDPR consents, newsletters, etc.) |
| POST | `/account/register` | Registers the current guest customer; the shopping cart and wishlist are kept |
| GET | `/account/check-username` | Checks whether a username is available |
| POST | `/account/activation` | Activates an account (registration type "Email validation"); the frontend page of the activation email link calls this endpoint |
| POST | `/account/password-recovery` | Sends the password recovery email; always succeeds, so it can't be used to find out whether an account exists |
| POST | `/account/password-recovery/check` | Checks the values of a password recovery link before the new password form is displayed |
| POST | `/account/password-recovery/confirm` | Sets a new password from the password recovery link; the customer is logged in and all its other tokens are revoked |
| GET | `/account/navigation` | Gets the account navigation (available tabs of "My account" depending on the settings) |
| GET | `/account/info` | Gets the customer info of the current customer |
| PUT | `/account/info` | Updates the customer info of the current customer |
| POST | `/account/email-revalidation` | Confirms a changed email address (registration type "Email validation"); the frontend page of the email revalidation link calls this endpoint |
| POST | `/account/change-password` | Changes the password of the current customer; all other tokens of the customer are revoked |
| GET | `/account/addresses` | Gets the addresses of the current customer |
| POST | `/account/addresses` | Adds an address to the current customer |
| PUT | `/account/addresses/{addressId}` | Updates an address of the current customer |
| DELETE | `/account/addresses/{addressId}` | Deletes an address of the current customer |
| GET | `/account/orders` | Gets the orders of the current customer |
| GET | `/account/orders/{orderId}` | Gets the details of an order of the current customer |

#### GET `/account/register`

Gets the registration form (enabled and required fields, available countries, custom attributes, GDPR consents, newsletters, etc.)

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Antworten:** 200 `RegisterModel`, 403

#### POST `/account/register`

Registers the current guest customer; the shopping cart and wishlist are kept

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Body:** `RegisterRequest`: Registration data, custom customer attributes and GDPR consents
- **Antworten:** 200 `RegistrationResultModel`, 400, 403

#### GET `/account/check-username`

Checks whether a username is available

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Parameter:**
  - `username` (Query, `string`): Username
- **Antworten:** 200 `UsernameAvailabilityModel`

#### POST `/account/activation`

Activates an account (registration type "Email validation"); the frontend page of the activation email link calls this endpoint

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Body:** `AccountTokenRequest`: Values of the activation link
- **Antworten:** 200 `AccountResultModel`, 400

#### POST `/account/password-recovery`

Sends the password recovery email; always succeeds, so it can't be used to find out whether an account exists

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Body:** `PasswordRecoveryModel`: Password recovery data
- **Antworten:** 204, 400

#### POST `/account/password-recovery/check`

Checks the values of a password recovery link before the new password form is displayed

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Body:** `AccountTokenRequest`: Values of the password recovery link
- **Antworten:** 200 `PasswordRecoveryConfirmModel`, 400

#### POST `/account/password-recovery/confirm`

Sets a new password from the password recovery link; the customer is logged in and all its other tokens are revoked

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Body:** `PasswordRecoveryConfirmRequest`: Values of the password recovery link and the new password
- **Antworten:** 200 `AccountResultModel`, 400

#### GET `/account/navigation`

Gets the account navigation (available tabs of "My account" depending on the settings)

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `selectedTabId` (Query, `int`): Identifier of the selected tab
- **Antworten:** 200 `CustomerNavigationModel`, 403

#### GET `/account/info`

Gets the customer info of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Antworten:** 200 `CustomerInfoModel`, 403

#### PUT `/account/info`

Updates the customer info of the current customer

> If the email is changed and the registration type is "Email validation", the new email has to be confirmed first (see "emailToRevalidate"); the email link calls POST account/email-revalidation. If login by phone is enabled and the phone number has changed, "phoneSmsVerified" is reset.

- **Anmeldung:** Token eines registrierten Kunden
- **Body:** `CustomerInfoRequest`: Customer info, custom customer attributes and GDPR consents
- **Antworten:** 200 `CustomerInfoModel`, 400, 403

#### POST `/account/email-revalidation`

Confirms a changed email address (registration type "Email validation"); the frontend page of the email revalidation link calls this endpoint

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Body:** `AccountTokenRequest`: Values of the email revalidation link
- **Antworten:** 200 `AccountResultModel`, 400

#### POST `/account/change-password`

Changes the password of the current customer; all other tokens of the customer are revoked

- **Anmeldung:** Token eines registrierten Kunden
- **Body:** `ChangePasswordModel`: Old and new password
- **Antworten:** 200 `TokenModel`, 400, 403

#### GET `/account/addresses`

Gets the addresses of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Antworten:** 200 `CustomerAddressListModel`, 403

#### POST `/account/addresses`

Adds an address to the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Body:** `AddressModel`: Address
- **Antworten:** 200 `CustomerAddressListModel`, 400, 403

#### PUT `/account/addresses/{addressId}`

Updates an address of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `addressId` (Pfad, `int`): Address identifier
- **Body:** `AddressModel`: Address
- **Antworten:** 200 `CustomerAddressListModel`, 400, 404

#### DELETE `/account/addresses/{addressId}`

Deletes an address of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `addressId` (Pfad, `int`): Address identifier
- **Antworten:** 200 `CustomerAddressListModel`, 404

#### GET `/account/orders`

Gets the orders of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `page` (Query, `int?`): Page number
  - `period` (Query, `OrderHistoryPeriods`): Period (All, Day, Week, Month, HalfYear, Year)
- **Antworten:** 200 `CustomerOrderListModel`, 403

#### GET `/account/orders/{orderId}`

Gets the details of an order of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `orderId` (Pfad, `int`): Order identifier
- **Antworten:** 200 `OrderDetailsModel`, 404

### Kundenkonto: Avatar, DSGVO, Geschenkkarten, Downloads

Represents the endpoints of the "My account" tools (avatar, GDPR tools, gift card balance, downloadable products)

CAPTCHA is not supported by the API

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/account/avatar` | Gets the avatar of the current customer |
| POST | `/account/avatar` | Uploads the avatar of the current customer (multipart/form-data, field "uploadedFile"; JPEG or GIF) |
| DELETE | `/account/avatar` | Removes the avatar of the current customer |
| GET | `/account/gdpr` | Gets the GDPR tools of the current customer |
| POST | `/account/gdpr/export` | Exports the personal data of the current customer (XLSX file) |
| POST | `/account/gdpr/delete` | Requests the deletion of the current customer account (the store owner is notified) |
| POST | `/account/gift-card-balance` | Checks the balance of a gift card |
| GET | `/account/downloadable-products` | Gets the downloadable products purchased by the current customer |
| GET | `/account/user-agreement/{orderItemGuid}` | Gets the user agreement of a downloadable product; the agreement has to be accepted before the download (GET downloads/order-item/{orderItemGuid}?agree=true) |

#### GET `/account/avatar`

Gets the avatar of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Antworten:** 200 `CustomerAvatarModel`, 403

#### POST `/account/avatar`

Uploads the avatar of the current customer (multipart/form-data, field "uploadedFile"; JPEG or GIF)

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `uploadedFile` (Form-Data (multipart), `IFormFile`): Avatar image
- **Antworten:** 200 `CustomerAvatarModel`, 400, 403

#### DELETE `/account/avatar`

Removes the avatar of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Antworten:** 200 `CustomerAvatarModel`, 403

#### GET `/account/gdpr`

Gets the GDPR tools of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Antworten:** 200 `GdprToolsModel`, 403

#### POST `/account/gdpr/export`

Exports the personal data of the current customer (XLSX file)

- **Anmeldung:** Token eines registrierten Kunden
- **Antworten:** 200, 403

#### POST `/account/gdpr/delete`

Requests the deletion of the current customer account (the store owner is notified)

- **Anmeldung:** Token eines registrierten Kunden
- **Antworten:** 200 `GdprToolsModel`, 403

#### POST `/account/gift-card-balance`

Checks the balance of a gift card

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `CheckGiftCardBalanceModel`: Gift card code
- **Antworten:** 200 `CheckGiftCardBalanceModel`, 400, 403

#### GET `/account/downloadable-products`

Gets the downloadable products purchased by the current customer

> The files are downloaded by GET downloads/order-item/{orderItemGuid} and GET downloads/license/{orderItemGuid}

- **Anmeldung:** Token eines registrierten Kunden
- **Antworten:** 200 `CustomerDownloadableProductsModel`, 403

#### GET `/account/user-agreement/{orderItemGuid}`

Gets the user agreement of a downloadable product; the agreement has to be accepted before the download (GET downloads/order-item/{orderItemGuid}?agree=true)

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `orderItemGuid` (Pfad, `Guid`): Order item GUID
- **Antworten:** 200 `UserAgreementModel`, 404

### Bestellungen, Abos, Bonuspunkte, Retouren

Represents the order related endpoints of "My account" (PDF invoice, re-order, cancellation, shipments, recurring payments, reward points, return requests)

Order list and details are provided by AccountApiController. Return requests can also be submitted by guests using the withdrawal token of the email link (see POST return-requests/find).

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/account/orders/{orderId}/pdf` | Gets the PDF invoice of an order of the current customer |
| POST | `/account/orders/{orderId}/reorder` | Adds the products of an order of the current customer to the shopping cart |
| POST | `/account/orders/{orderId}/cancel` | Cancels an order of the current customer (only if "Allow customers to cancel orders" is enabled) |
| GET | `/account/shipments/{shipmentId}` | Gets the details of a shipment of an order of the current customer |
| GET | `/account/recurring-payments` | Gets the recurring payments of the current customer |
| POST | `/account/recurring-payments/{recurringPaymentId}/cancel` | Cancels a recurring payment of the current customer |
| POST | `/account/recurring-payments/{recurringPaymentId}/retry` | Retries the last failed payment of a recurring payment of the current customer |
| GET | `/account/reward-points` | Gets the reward points history of the current customer |
| GET | `/account/return-requests` | Gets the return requests of the current customer |
| GET | `/account/orders/{orderId}/return-request` | Gets the return request form of an order (returnable items, reasons, actions) |
| POST | `/account/orders/{orderId}/return-request` | Submits a return request (withdrawal) for items of an order |

#### GET `/account/orders/{orderId}/pdf`

Gets the PDF invoice of an order of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `orderId` (Pfad, `int`): Order identifier
- **Antworten:** 200, 404

#### POST `/account/orders/{orderId}/reorder`

Adds the products of an order of the current customer to the shopping cart

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `orderId` (Pfad, `int`): Order identifier
- **Antworten:** 200 `ReorderResultModel`, 404

#### POST `/account/orders/{orderId}/cancel`

Cancels an order of the current customer (only if "Allow customers to cancel orders" is enabled)

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `orderId` (Pfad, `int`): Order identifier
- **Antworten:** 200 `OrderDetailsModel`, 400, 403, 404

#### GET `/account/shipments/{shipmentId}`

Gets the details of a shipment of an order of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `shipmentId` (Pfad, `int`): Shipment identifier
- **Antworten:** 200 `ShipmentDetailsModel`, 404

#### GET `/account/recurring-payments`

Gets the recurring payments of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Antworten:** 200 `CustomerRecurringPaymentListModel`, 403

#### POST `/account/recurring-payments/{recurringPaymentId}/cancel`

Cancels a recurring payment of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `recurringPaymentId` (Pfad, `int`): Recurring payment identifier
- **Antworten:** 200 `CustomerRecurringPaymentListModel`, 403, 404

#### POST `/account/recurring-payments/{recurringPaymentId}/retry`

Retries the last failed payment of a recurring payment of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `recurringPaymentId` (Pfad, `int`): Recurring payment identifier
- **Antworten:** 200 `CustomerRecurringPaymentListModel`, 403, 404

#### GET `/account/reward-points`

Gets the reward points history of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `page` (Query, `int?`): Page number
- **Antworten:** 200 `CustomerRewardPointsModel`, 403

#### GET `/account/return-requests`

Gets the return requests of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Antworten:** 200 `CustomerReturnRequestsModel`, 403

#### GET `/account/orders/{orderId}/return-request`

Gets the return request form of an order (returnable items, reasons, actions)

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `orderId` (Pfad, `int`): Order identifier
  - `token` (Query, `string`): Withdrawal token of the email link (guest return requests); empty for the own orders of the current customer
- **Antworten:** 200 `SubmitReturnRequestModel`, 403, 404

#### POST `/account/orders/{orderId}/return-request`

Submits a return request (withdrawal) for items of an order

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `orderId` (Pfad, `int`): Order identifier
- **Body:** `SubmitReturnRequestRequest`: Return request
- **Antworten:** 200 `SubmitReturnRequestModel`, 400, 403, 404

### Retouren (Upload, Gast-Widerruf)

Represents the return request endpoints that are not bound to an order of the current customer (file upload, guest withdrawal form)

The return request form and submission are provided by CustomerOrdersApiController (GET/POST account/orders/{orderId}/return-request). CAPTCHA is not supported by the API.

| Methode | Pfad | Beschreibung |
|---|---|---|
| POST | `/return-requests/upload` | Uploads a file for a return request (multipart/form-data, field "file") |
| GET | `/return-requests/find` | Gets the withdrawal form for guests (find an order by order number and billing email) |
| POST | `/return-requests/find` | Sends the withdrawal link (return request of a guest order) to the billing email of the order; always returns the same confirmation text, so it can't be used to find out whether an order exists |

#### POST `/return-requests/upload`

Uploads a file for a return request (multipart/form-data, field "file")

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `file` (Form-Data (multipart), `IFormFile`): File
- **Antworten:** 200 `ReturnRequestFileModel`, 400, 403

#### GET `/return-requests/find`

Gets the withdrawal form for guests (find an order by order number and billing email)

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `WithdrawalFormModel`, 403

#### POST `/return-requests/find`

Sends the withdrawal link (return request of a guest order) to the billing email of the order; always returns the same confirmation text, so it can't be used to find out whether an order exists

> The email link has to point to a frontend page that calls GET/POST account/orders/{orderId}/return-request with the token

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `WithdrawalFormModel`: Order number and billing email
- **Antworten:** 200 `WithdrawalFormModel`, 400, 403

### Downloads

Represents the download endpoints (product samples, purchased downloadable products, license files, order note files, uploaded files)

The endpoints return the file itself; if the download is stored as an external URL, a JSON object with the "downloadUrl" is returned instead (status 200, content type application/json), so the frontend has to check the content type of the response.

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/downloads/sample/{productId}` | Downloads the sample file of a product |
| GET | `/downloads/order-item/{orderItemGuid}` | Downloads a purchased downloadable product |
| GET | `/downloads/license/{orderItemGuid}` | Downloads the license file of a purchased downloadable product |
| GET | `/downloads/order-note/{orderNoteId}` | Downloads the file attached to an order note of an order of the current customer |
| GET | `/downloads/file/{downloadGuid}` | Downloads a file uploaded by a customer (e.g. for a product or checkout attribute of the type "File upload") |

#### GET `/downloads/sample/{productId}`

Downloads the sample file of a product

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `productId` (Pfad, `int`): Product identifier
- **Antworten:** 200 `DownloadUrlModel`, 404

#### GET `/downloads/order-item/{orderItemGuid}`

Downloads a purchased downloadable product

> Returns 409 (Conflict) if the product has a user agreement and "agree" is not set

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `orderItemGuid` (Pfad, `Guid`): Order item GUID
  - `agree` (Query, `bool`): Whether the customer has accepted the user agreement of the product (see GET account/user-agreement/{orderItemGuid})
- **Antworten:** 200 `DownloadUrlModel`, 403, 404, 409

#### GET `/downloads/license/{orderItemGuid}`

Downloads the license file of a purchased downloadable product

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `orderItemGuid` (Pfad, `Guid`): Order item GUID
- **Antworten:** 200 `DownloadUrlModel`, 403, 404

#### GET `/downloads/order-note/{orderNoteId}`

Downloads the file attached to an order note of an order of the current customer

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `orderNoteId` (Pfad, `int`): Order note identifier
- **Antworten:** 200 `DownloadUrlModel`, 404

#### GET `/downloads/file/{downloadGuid}`

Downloads a file uploaded by a customer (e.g. for a product or checkout attribute of the type "File upload")

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `downloadGuid` (Pfad, `Guid`): Download GUID
- **Antworten:** 200 `DownloadUrlModel`, 404

### Newsletter

Represents the newsletter endpoints (subscribe/unsubscribe box and the confirmation of the email link)

The newsletter subscriptions of registered customers are managed by PUT account/info. CAPTCHA is not supported by the API.

| Methode | Pfad | Beschreibung |
|---|---|---|
| POST | `/newsletter/subscribe` | Subscribes an email to the newsletter (all subscription types ticked by default) or unsubscribes it; in both cases an email with a confirmation link is sent |
| POST | `/newsletter/activation` | Activates or deletes the newsletter subscriptions of the email link |

#### POST `/newsletter/subscribe`

Subscribes an email to the newsletter (all subscription types ticked by default) or unsubscribes it; in both cases an email with a confirmation link is sent

> The email link has to point to a frontend page that calls POST newsletter/activation

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `NewsletterSubscribeRequest`: Email and whether to subscribe or unsubscribe
- **Antworten:** 200 `NewsletterSubscribeResultModel`, 400

#### POST `/newsletter/activation`

Activates or deletes the newsletter subscriptions of the email link

- **Anmeldung:** Token (Gast oder Kunde)
- **Body:** `NewsletterActivationRequest`: Token and whether to activate or delete the subscriptions
- **Antworten:** 200 `SubscriptionActivationModel`, 404

### Händlerkonto

Represents the vendor account endpoints of the current customer (apply for a vendor account, edit the vendor info)

The changing endpoints accept multipart/form-data with the same fields as the storefront form: "Name", "Email", "Description", the custom vendor attributes ("vendor_attribute_{id}") and the picture ("uploadedFile"). CAPTCHA is not supported by the API.

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/vendor/apply` | Gets the form to apply for a vendor account |
| POST | `/vendor/apply` | Applies for a vendor account (multipart/form-data); the vendor is created inactive and the store owner is notified |
| GET | `/vendor/info` | Gets the vendor info of the current customer (only for vendors if "Allow vendors to edit info" is enabled) |
| PUT | `/vendor/info` | Updates the vendor info of the current customer (multipart/form-data); an uploaded picture replaces the current one |
| DELETE | `/vendor/info/picture` | Removes the picture of the vendor of the current customer |

#### GET `/vendor/apply`

Gets the form to apply for a vendor account

- **Anmeldung:** Token eines registrierten Kunden
- **Antworten:** 200 `ApplyVendorModel`, 403

#### POST `/vendor/apply`

Applies for a vendor account (multipart/form-data); the vendor is created inactive and the store owner is notified

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `model` (Form-Data (multipart), `ApplyVendorModel`): Vendor name, email and description
  - `uploadedFile` (Form-Data (multipart), `IFormFile`): Vendor picture (optional)
- **Antworten:** 200 `ApplyVendorModel`, 400, 403

#### GET `/vendor/info`

Gets the vendor info of the current customer (only for vendors if "Allow vendors to edit info" is enabled)

- **Anmeldung:** Token eines registrierten Kunden
- **Antworten:** 200 `VendorInfoModel`, 403

#### PUT `/vendor/info`

Updates the vendor info of the current customer (multipart/form-data); an uploaded picture replaces the current one

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `model` (Form-Data (multipart), `VendorInfoModel`): Vendor name, email and description
  - `uploadedFile` (Form-Data (multipart), `IFormFile`): Vendor picture (optional)
- **Antworten:** 200 `VendorInfoModel`, 400, 403

#### DELETE `/vendor/info/picture`

Removes the picture of the vendor of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Antworten:** 200 `VendorInfoModel`, 403

### Private Nachrichten

Represents the private message endpoints of the current customer (only if private messages are enabled)

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/private-messages` | Gets the received (inbox) or sent private messages of the current customer |
| GET | `/private-messages/{privateMessageId}` | Gets a private message of the current customer; a received message is marked as read |
| GET | `/private-messages/new` | Gets the form to send a private message |
| POST | `/private-messages` | Sends a private message; on a reply ("replyToMessageId") the recipient is taken from the original message |
| DELETE | `/private-messages/{privateMessageId}` | Deletes a private message for the current customer (as author and/or recipient) |
| POST | `/private-messages/{privateMessageId}/mark-unread` | Marks a received private message as unread |

#### GET `/private-messages`

Gets the received (inbox) or sent private messages of the current customer

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `tab` (Query, `string`): "inbox" (default) or "sent"
  - `page` (Query, `int`): Page number
- **Antworten:** 200 `PrivateMessageListModel`, 403

#### GET `/private-messages/{privateMessageId}`

Gets a private message of the current customer; a received message is marked as read

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `privateMessageId` (Pfad, `int`): Private message identifier
- **Antworten:** 200 `PrivateMessageModel`, 403, 404

#### GET `/private-messages/new`

Gets the form to send a private message

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `toCustomerId` (Query, `int`): Recipient customer identifier
  - `replyToMessageId` (Query, `int?`): Identifier of the message to reply to (optional)
- **Antworten:** 200 `SendPrivateMessageModel`, 403, 404

#### POST `/private-messages`

Sends a private message; on a reply ("replyToMessageId") the recipient is taken from the original message

- **Anmeldung:** Token eines registrierten Kunden
- **Body:** `SendPrivateMessageModel`: Recipient, subject and message
- **Antworten:** 200 `PrivateMessageModel`, 400, 403, 404

#### DELETE `/private-messages/{privateMessageId}`

Deletes a private message for the current customer (as author and/or recipient)

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `privateMessageId` (Pfad, `int`): Private message identifier
- **Antworten:** 204, 403, 404

#### POST `/private-messages/{privateMessageId}/mark-unread`

Marks a received private message as unread

- **Anmeldung:** Token eines registrierten Kunden
- **Parameter:**
  - `privateMessageId` (Pfad, `int`): Private message identifier
- **Antworten:** 204, 403, 404

### Öffentliche Kundenprofile

Represents the public customer profile endpoints (only if "Allow viewing profiles" is enabled)

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/profiles/{customerId}` | Gets the public profile of a customer |

#### GET `/profiles/{customerId}`

Gets the public profile of a customer

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `customerId` (Pfad, `int`): Customer identifier
- **Antworten:** 200 `CustomerProfileModel`, 403, 404

### Kontaktformulare

Represents the contact endpoints (contact us, contact vendor)

CAPTCHA is not supported by the API, so it's not validated even if enabled for the contact forms (DisplayCaptcha is always false)

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/contact-us` | Gets the contact us form (prefilled with the customer data, incl. the custom contact form attributes) |
| POST | `/contact-us` | Sends the contact us enquiry to the store owner |
| GET | `/vendors/{vendorId}/contact` | Gets the contact vendor form (prefilled with the customer data) |
| POST | `/vendors/{vendorId}/contact` | Sends the enquiry to the vendor |

#### GET `/contact-us`

Gets the contact us form (prefilled with the customer data, incl. the custom contact form attributes)

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Antworten:** 200 `ContactUsModel`, 403

#### POST `/contact-us`

Sends the contact us enquiry to the store owner

- **Anmeldung:** Token (Gast oder Kunde) · auch bei geschlossenem Shop bzw. ohne Shop-Zugriffsrecht erreichbar
- **Body:** `ContactUsModel`: Contact us model (Email, FullName, Subject if enabled, Enquiry, selected ContactFormAttributes)
- **Antworten:** 200 `ContactUsModel`, 400, 403

#### GET `/vendors/{vendorId}/contact`

Gets the contact vendor form (prefilled with the customer data)

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `vendorId` (Pfad, `int`): Vendor identifier
- **Antworten:** 200 `ContactVendorModel`, 403, 404

#### POST `/vendors/{vendorId}/contact`

Sends the enquiry to the vendor

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `vendorId` (Pfad, `int`): Vendor identifier
- **Body:** `ContactVendorModel`: Contact vendor model (Email, FullName, Subject if enabled, Enquiry)
- **Antworten:** 200 `ContactVendorModel`, 400, 403, 404

### Blog

Represents the blog endpoints

CAPTCHA is not supported by the API, so it's not validated for blog comments even if enabled

| Methode | Pfad | Beschreibung |
|---|---|---|
| GET | `/blog` | Gets the blog posts of the working language |
| GET | `/blog/{blogPostId}` | Gets a blog post incl. approved comments |
| POST | `/blog/{blogPostId}/comments` | Adds a comment to a blog post |
| GET | `/blog/tags` | Gets the most popular blog tags of the working language (like the BlogTags view component) |
| GET | `/blog/months` | Gets the years and months with blog posts of the working language (like the BlogMonths view component) |

#### GET `/blog`

Gets the blog posts of the working language

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `command` (Query, `BlogPagingFilteringModel`): Paging and filtering: PageNumber (starting with 1), PageSize (default "Posts page size" setting),
    /// Tag (posts with the tag) or Month (posts of the month, format "yyyy-M", e.g. "2026-10")
- **Antworten:** 200 `BlogPostListModel`, 400, 403

#### GET `/blog/{blogPostId}`

Gets a blog post incl. approved comments

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `blogPostId` (Pfad, `int`): Blog post identifier
- **Antworten:** 200 `BlogPostModel`, 403, 404

#### POST `/blog/{blogPostId}/comments`

Adds a comment to a blog post

- **Anmeldung:** Token (Gast oder Kunde)
- **Parameter:**
  - `blogPostId` (Pfad, `int`): Blog post identifier
- **Body:** `BlogPostModel`: Blog post model; only AddNewComment.CommentText is used
- **Antworten:** 200 `BlogCommentResultModel`, 400, 403, 404

#### GET `/blog/tags`

Gets the most popular blog tags of the working language (like the BlogTags view component)

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `BlogPostTagListModel`, 403

#### GET `/blog/months`

Gets the years and months with blog posts of the working language (like the BlogMonths view component)

- **Anmeldung:** Token (Gast oder Kunde)
- **Antworten:** 200 `IList<BlogPostYearModel>`, 403

## Anhang: Modelle des Plugins

Request- und Response-Modelle, die das Plugin selbst definiert (Namespace `Nop.Plugin.Misc.HeadlessApi.Models`). Alle anderen Modelle in den Antworten sind die Storefront-Modelle von nopCommerce (`Nop.Web.Models.*`, z. B. `ProductDetailsModel`); ihre Felder stehen im Quellcode unter `src/Presentation/Nop.Web/Models`. Feldnamen werden in JSON in camelCase geschrieben.

### AccountResultModel (Account)

Represents the result of an account action (account activation, email revalidation, password recovery)

| Feld | Typ | Beschreibung |
|---|---|---|
| `message` | `string` | message to display to the customer |
| `token` | `TokenModel` | token of the customer; only set if the customer has been logged in by the action, so the frontend has to use this token from now on |

### AccountTokenRequest (Account)

Represents a request containing the values of an email link (account activation, email revalidation, password recovery)

| Feld | Typ | Beschreibung |
|---|---|---|
| `token` | `string` | token from the link |
| `email` | `string` | customer email from the link (used by links of previous versions) |
| `guid` | `Guid` | customer GUID from the link |

### CustomerInfoRequest (Account)

Represents a request to update the customer info; it's the customer info form of the storefront extended by the custom form fields

Erweitert `CustomerInfoModel`, enthält also auch dessen Felder.

| Feld | Typ | Beschreibung |
|---|---|---|
| `attributes` | `Dictionary<string, string>` | custom customer attributes and GDPR consents, using the same keys as the storefront form, e.g. "customer_attribute_1": "5" (value id), "customer_attribute_2": "6,7" (checkboxes), "customer_attribute_3": "text", "consent2": "on" (accepted GDPR consent; "true" is accepted as well) |

### PasswordRecoveryConfirmRequest (Account)

Represents a request to set a new password from the password recovery link

Erweitert `PasswordRecoveryConfirmModel`, enthält also auch dessen Felder.

| Feld | Typ | Beschreibung |
|---|---|---|
| `token` | `string` | token from the link |
| `email` | `string` | customer email from the link (used by links of previous versions) |
| `guid` | `Guid` | customer GUID from the link |

### RegisterRequest (Account)

Represents a registration request; it's the registration form of the storefront extended by the custom form fields

Erweitert `RegisterModel`, enthält also auch dessen Felder.

| Feld | Typ | Beschreibung |
|---|---|---|
| `attributes` | `Dictionary<string, string>` | custom customer attributes and GDPR consents, using the same keys as the storefront form, e.g. "customer_attribute_1": "5" (value id), "customer_attribute_2": "6,7" (checkboxes), "customer_attribute_3": "text", "consent2": "on" (accepted GDPR consent; "true" is accepted as well) |

### RegistrationResultModel (Account)

Represents the result of a registration

| Feld | Typ | Beschreibung |
|---|---|---|
| `registrationType` | `string` | registration type of the store (Standard, EmailValidation, AdminApproval) |
| `message` | `string` | message to display to the customer |
| `token` | `TokenModel` | token of the registered customer; only set if the customer is logged in right away (registration type "Standard") |

### UsernameAvailabilityModel (Account)

Represents the result of the username availability check

| Feld | Typ | Beschreibung |
|---|---|---|
| `available` | `bool` | whether the username is available |
| `text` | `string` | status text to display to the customer |

### LoginRequest (Auth)

Represents a login request

| Feld | Typ | Beschreibung |
|---|---|---|
| `email` | `string` |  |
| `username` | `string` |  |
| `password` | `string` |  |

### TokenModel (Auth)

Represents an issued API token

| Feld | Typ | Beschreibung |
|---|---|---|
| `accessToken` | `string` |  |
| `tokenType` | `string` |  |
| `expiresOnUtc` | `DateTime` |  |
| `customerGuid` | `Guid` |  |
| `isRegistered` | `bool` |  |

### BlogCommentResultModel (Blog)

Represents the result of adding a blog comment

| Feld | Typ | Beschreibung |
|---|---|---|
| `isApproved` | `bool` | whether the comment is approved (otherwise it's displayed after the approval by the store owner) |
| `result` | `string` | result message |
| `blogPost` | `BlogPostModel` | updated blog post (incl. comments) |

### FilterLevelValuesModel (Catalog)

Represents the available filter level values (cascading selection: level 1, then level 2, then level 3)

| Feld | Typ | Beschreibung |
|---|---|---|
| `defaultItemText` | `string` | text of the default (empty) item of the selection lists |
| `values` | `IList<FilterLevelValueItemModel>` | filter level values |

### FilterLevelValueItemModel (Catalog)

Represents a filter level value

| Feld | Typ | Beschreibung |
|---|---|---|
| `filterLevel1Value` | `string` |  |
| `filterLevel2Value` | `string` | level 2 value; null as long as no level 1 value is selected |
| `filterLevel3Value` | `string` | level 3 value; null as long as no level 2 value is selected |

### CheckoutAddressRequest (Checkout)

Represents a request to set the billing or shipping address of the checkout

| Feld | Typ | Beschreibung |
|---|---|---|
| `addressId` | `int` | identifier of an existing address of the customer; 0 to use the new address |
| `newAddress` | `AddressModel` | new address (used if AddressId is 0) |
| `attributes` | `Dictionary<string, string>` | custom address attributes, using the same keys as the storefront form (e.g. "address_attribute_1": "2") |
| `shipToSameAddress` | `bool` | whether to ship to the billing address (billing address only) |
| `vatNumber` | `string` | VAT number (billing address of guests only, if EU VAT is enabled) |
| `pickupInStore` | `bool` | whether to pick up the order in store instead of shipping (shipping address only) |
| `pickupPoint` | `string` | selected pickup point as "{pickupPointId}___{providerSystemName}" (see the pickup points of the shipping address model) |

### CheckoutStepModel (Checkout)

Represents the result of a checkout step: the next step the frontend should display and its data

| Feld | Typ | Beschreibung |
|---|---|---|
| `nextStep` | `string` | next step: billing_address, shipping_address, shipping_method, payment_method, payment_info, confirm |
| `model` | `object` | model of the next step (e.g. CheckoutShippingMethodModel for shipping_method) |
| `warnings` | `IList<string>` | warnings to display (e.g. an invalid VAT number), the step has been saved anyway |

### PaymentInfoModel (Checkout)

Represents the payment info step

| Feld | Typ | Beschreibung |
|---|---|---|
| `paymentMethodSystemName` | `string` |  |
| `paymentMethodName` | `string` |  |
| `paymentMethodType` | `string` | payment method type (Standard, Redirection, Button) |
| `skipPaymentInfo` | `bool` | whether the payment info step can be skipped (no form fields) |
| `paymentMethodDescription` | `string` |  |
| `displayOrderTotals` | `bool` |  |

### PaymentInfoRequest (Checkout)

Represents a request to enter the payment information

| Feld | Typ | Beschreibung |
|---|---|---|
| `values` | `Dictionary<string, string>` |  |

### PaymentMethodRequest (Checkout)

Represents a request to select the payment method of the checkout

| Feld | Typ | Beschreibung |
|---|---|---|
| `paymentMethodSystemName` | `string` |  |
| `useRewardPoints` | `bool` |  |

### PlaceOrderResultModel (Checkout)

Represents the result of placing an order (or re-posting a payment)

| Feld | Typ | Beschreibung |
|---|---|---|
| `orderId` | `int` |  |
| `customOrderNumber` | `string` |  |
| `paymentRedirectUrl` | `string` | URL of the payment provider the frontend has to redirect the customer to (redirection payment methods); after the payment the provider returns the customer to the storefront, which redirects to the frontend if configured |
| `completedUrl` | `string` | URL of the frontend "checkout completed" page (if configured) |

### ShippingMethodRequest (Checkout)

Represents a request to select the shipping method of the checkout

| Feld | Typ | Beschreibung |
|---|---|---|
| `shippingOption` | `string` | selected shipping option as "{name}___{shippingRateComputationMethodSystemName}" (as in the storefront) |
| `desiredDeliveryDate` | `DateTime?` | desired delivery date (only if customers are allowed to choose it) |
| `pickupInStore` | `bool` | whether to pick up the order in store (if pickup is displayed on the shipping method step) |
| `pickupPoint` | `string` | selected pickup point as "{pickupPointId}___{providerSystemName}" |

### LayoutModel (Common)

Represents the data of the common layout (header, footer, selectors) composed of the storefront view component models

| Feld | Typ | Beschreibung |
|---|---|---|
| `logo` | `LogoModel` |  |
| `headerLinks` | `HeaderLinksModel` |  |
| `footer` | `FooterModel` |  |
| `social` | `SocialModel` |  |
| `faviconAndAppIcons` | `FaviconAndAppIconsModel` | favicon and app icons; HeadCode contains the HTML tags configured by the store owner to be added to the head element |
| `languageSelector` | `LanguageSelectorModel` | language selector (the storefront hides it if there is only one language) |
| `currencySelector` | `CurrencySelectorModel` | currency selector (the storefront hides it if there is only one currency) |
| `taxTypeSelector` | `TaxTypeSelectorModel` | tax type selector; null if customers are not allowed to select the tax display type |
| `searchBox` | `SearchBoxModel` |  |
| `storeClosed` | `bool` | whether the store is closed |
| `canAccessClosedStore` | `bool` | whether the current customer can access the store although it is closed (e.g. administrators) |
| `navigationAllowed` | `bool` | whether the current customer can navigate the public store (otherwise only login/registration are available) |
| `displayEuCookieLawWarning` | `bool` | whether to display the EU cookie law warning to the current customer |
| `miniShoppingCartEnabled` | `bool` | whether to display the mini shopping cart (flyout cart) |
| `blogEnabled` | `bool` |  |
| `sitemapEnabled` | `bool` |  |
| `allowCustomersToContactVendors` | `bool` |  |

### UrlResolveModel (Common)

Represents the result of resolving an SEO friendly URL (slug)

| Feld | Typ | Beschreibung |
|---|---|---|
| `entityName` | `string` | entity name (e.g. Product, Category, Manufacturer, Vendor, Topic) |
| `entityId` | `int` |  |
| `languageId` | `int` |  |
| `redirectToSlug` | `string` | slug the frontend should redirect to (permanently), if the requested one is no longer active |

### ContextModel (Context)

Represents the context of the current request (customer, store, language, currency, tax display type)

| Feld | Typ | Beschreibung |
|---|---|---|
| `customer` | `CustomerInfoModel` |  |
| `storeId` | `int` |  |
| `storeName` | `string` |  |
| `workingLanguage` | `LanguageModel` |  |
| `workingCurrency` | `CurrencyModel` |  |
| `availableLanguages` | `IList<LanguageModel>` |  |
| `availableCurrencies` | `IList<CurrencyModel>` |  |
| `taxDisplayType` | `TaxDisplayType` | tax display type of the current customer (prices including or excluding tax) |
| `allowCustomersToSelectTaxDisplayType` | `bool` | whether the customer is allowed to change the tax display type |

### CurrencyModel (Context)

Represents a currency model

| Feld | Typ | Beschreibung |
|---|---|---|
| `id` | `int` |  |
| `name` | `string` |  |
| `currencyCode` | `string` |  |

### CustomerInfoModel (Context)

Represents the basic information about the current customer

| Feld | Typ | Beschreibung |
|---|---|---|
| `customerGuid` | `Guid` |  |
| `isRegistered` | `bool` |  |
| `email` | `string` |  |
| `username` | `string` |  |
| `fullName` | `string` |  |

### LanguageModel (Context)

Represents a language model

| Feld | Typ | Beschreibung |
|---|---|---|
| `id` | `int` |  |
| `name` | `string` |  |
| `languageCulture` | `string` |  |
| `uniqueSeoCode` | `string` |  |
| `rtl` | `bool` |  |

### SetCurrencyRequest (Context)

Represents a request to change the working currency

| Feld | Typ | Beschreibung |
|---|---|---|
| `currencyId` | `int` |  |

### SetLanguageRequest (Context)

Represents a request to change the working language

| Feld | Typ | Beschreibung |
|---|---|---|
| `languageId` | `int` |  |

### SetTaxTypeRequest (Context)

Represents a request to change the tax display type (prices including or excluding tax)

| Feld | Typ | Beschreibung |
|---|---|---|
| `taxDisplayType` | `TaxDisplayType` |  |

### CountryAddressType (Directory)

Represents the address type the countries are requested for

Werte: `All = 0`, `Billing = 10`, `Shipping = 20`

### CountryModel (Directory)

Represents a country available in the address forms

| Feld | Typ | Beschreibung |
|---|---|---|
| `id` | `int` |  |
| `name` | `string` | localized name |
| `twoLetterIsoCode` | `string` |  |
| `threeLetterIsoCode` | `string` |  |
| `allowsBilling` | `bool` | whether billing is allowed to this country |
| `allowsShipping` | `bool` | whether shipping is allowed to this country |
| `subjectToVat` | `bool` |  |

### DownloadUrlModel (Downloads)

Represents an external download; returned instead of the file when the download is stored as a URL

| Feld | Typ | Beschreibung |
|---|---|---|
| `downloadUrl` | `string` | URL of the file |

### NewsletterActivationRequest (Newsletter)

Represents a request to confirm or cancel a newsletter subscription (values of the email link)

| Feld | Typ | Beschreibung |
|---|---|---|
| `token` | `Guid` | subscription token (GUID) of the email link |
| `active` | `bool` | whether to activate (true) or to delete (false) the subscription |

### NewsletterSubscribeRequest (Newsletter)

Represents a request to subscribe to or unsubscribe from the newsletter

| Feld | Typ | Beschreibung |
|---|---|---|
| `email` | `string` | email |
| `subscribe` | `bool` | whether to subscribe (true) or unsubscribe (false) |

### NewsletterSubscribeResultModel (Newsletter)

Represents the result of a newsletter subscription request

| Feld | Typ | Beschreibung |
|---|---|---|
| `message` | `string` | message to display to the customer |

### ReorderResultModel (Orders)

Represents the result of a re-order (the products of an order are added to the shopping cart)

| Feld | Typ | Beschreibung |
|---|---|---|
| `warnings` | `IList<string>` | warnings of products that couldn't be added to the shopping cart |
| `message` | `string` | message to display to the customer if not all products could be added |

### AddProductReviewResultModel (Products)

Represents the result of adding a product review

| Feld | Typ | Beschreibung |
|---|---|---|
| `productReviewId` | `int` | identifier of the added review |
| `isApproved` | `bool` | whether the review is approved (visible) right away |
| `message` | `string` | message to display to the customer |
| `productReviews` | `ProductReviewsModel` | updated product reviews |

### BackInStockSubscriptionResultModel (Products)

Represents the result of a back in stock subscription toggle

| Feld | Typ | Beschreibung |
|---|---|---|
| `productId` | `int` |  |
| `subscribed` | `bool` | whether the customer is subscribed now (false if the customer has been unsubscribed) |
| `message` | `string` | message to display to the customer |

### ProductAttributeChangeModel (Products)

Represents the product details recalculated for the selected attributes

| Feld | Typ | Beschreibung |
|---|---|---|
| `productId` | `int` |  |
| `gtin` | `string` |  |
| `mpn` | `string` |  |
| `sku` | `string` |  |
| `price` | `string` | formatted unit price (including discounts) for the selected attributes; empty if prices are hidden or the customer enters the price |
| `priceValue` | `decimal?` | unit price (including discounts) for the selected attributes in the working currency |
| `oldPrice` | `string` | formatted old price of the product; null if there is no old price |
| `oldPriceValue` | `decimal?` | old price of the product in the working currency |
| `basePricePAngV` | `string` | formatted base price (PAngV) |
| `stockAvailability` | `string` |  |
| `enabledAttributeMappingIds` | `IList<int>` | identifiers of the attribute mappings whose conditions are met |
| `disabledAttributeMappingIds` | `IList<int>` | identifiers of the attribute mappings whose conditions are not met |
| `pictureFullSizeUrl` | `string` | full size URL of the picture of the selected combination or attribute values |
| `pictureDefaultSizeUrl` | `string` | default size URL (product details page) of the picture of the selected combination or attribute values |
| `pictureIds` | `IList<int>` | identifiers of the pictures of the selected combination or attribute values |
| `isFreeShipping` | `bool` |  |
| `messages` | `IList<string>` | warnings of the attribute parser (e.g. required attributes not selected yet) |

### ProductAttributeChangeRequest (Products)

Represents a request to recalculate the product details after the customer changed the selected attributes

| Feld | Typ | Beschreibung |
|---|---|---|
| `attributes` | `Dictionary<string, string>` | selected product attributes, using the same keys as the storefront form (see AddToCartRequest.Attributes), e.g. "product_attribute_5": "12" (value id), "product_attribute_6": "13,14" (checkboxes), "product_attribute_7": "text" |
| `rentalStartDate` | `DateTime?` | rental start date (only for rental products) |
| `rentalEndDate` | `DateTime?` | rental end date (only for rental products) |
| `validateAttributeConditions` | `bool` | whether to validate the attribute conditions (enabled/disabled attributes) |
| `loadPicture` | `bool` | whether to load the picture of the selected combination or attribute values |

### ProductEstimateShippingRequest (Products)

Represents a request to estimate the shipping of a product (product details page)

| Feld | Typ | Beschreibung |
|---|---|---|
| `countryId` | `int?` |  |
| `stateProvinceId` | `int?` |  |
| `zipPostalCode` | `string` |  |
| `city` | `string` |  |
| `quantity` | `int` |  |
| `attributes` | `Dictionary<string, string>` | selected product attributes, using the same keys as the storefront form (see AddToCartRequest.Attributes) |
| `customerEnteredPrice` | `decimal?` | price entered by the customer (only for products with "Customer enters price"), in the working currency |
| `rentalStartDate` | `DateTime?` | rental start date (only for rental products) |
| `rentalEndDate` | `DateTime?` | rental end date (only for rental products) |

### ReviewHelpfulnessRequest (Products)

Represents a request to vote for the helpfulness of a product review

| Feld | Typ | Beschreibung |
|---|---|---|
| `wasHelpful` | `bool` |  |

### ReviewHelpfulnessResultModel (Products)

Represents the result of a helpfulness vote for a product review

| Feld | Typ | Beschreibung |
|---|---|---|
| `productReviewId` | `int` |  |
| `result` | `string` | message to display to the customer |
| `totalYes` | `int` |  |
| `totalNo` | `int` |  |

### UploadedFileModel (Products)

Represents the result of a file upload for a "file upload" product attribute

| Feld | Typ | Beschreibung |
|---|---|---|
| `downloadGuid` | `Guid` | download GUID; pass it as value of the attribute (e.g. "product_attribute_5": "{downloadGuid}") when adding the product to the cart |
| `message` | `string` | message to display to the customer |

### CustomerProfileModel (Profiles)

Represents the public profile of a customer

| Feld | Typ | Beschreibung |
|---|---|---|
| `profile` | `ProfileIndexModel` | profile page model (title, paging of the latest forum posts) |
| `info` | `ProfileInfoModel` | profile info (avatar, location, join date, forum statistics, etc.) |

### ReturnRequestFileModel (ReturnRequests)

Represents the result of a file upload for a return request

| Feld | Typ | Beschreibung |
|---|---|---|
| `downloadGuid` | `Guid` | GUID of the uploaded file; pass it as "uploadedFileGuid" when submitting the return request |
| `message` | `string` | message to display to the customer |

### SubmitReturnRequestRequest (ReturnRequests)

Represents a request to submit a return request (withdrawal) for items of an order

| Feld | Typ | Beschreibung |
|---|---|---|
| `quantities` | `Dictionary<int, int>` | quantities to return by order item identifier, e.g. { "12": 1, "13": 2 }; items with quantity 0 are ignored |
| `returnRequestReasonId` | `int` | return reason identifier |
| `returnRequestActionId` | `int` | requested action identifier |
| `comments` | `string` | comments |
| `uploadedFileGuid` | `Guid` | GUID of the file uploaded by POST return-requests/upload |
| `withdrawalToken` | `string` | withdrawal token of the email link (guest return requests); empty for registered customers |

### AddToCartRequest (ShoppingCart)

Represents a request to add a product to the shopping cart or wishlist

| Feld | Typ | Beschreibung |
|---|---|---|
| `productId` | `int` |  |
| `quantity` | `int` |  |
| `attributes` | `Dictionary<string, string>` | selected product attributes and gift card fields, using the same keys as the storefront form, e.g. "product_attribute_5": "12" (value id), "product_attribute_6": "13,14" (checkboxes), "product_attribute_7": "text", "giftcard_1.RecipientName": "...", "giftcard_1.RecipientEmail": "...", "giftcard_1.SenderName": "...", "giftcard_1.Message": "..." |
| `customerEnteredPrice` | `decimal?` | price entered by the customer (only for products with "Customer enters price"), in the working currency |
| `rentalStartDate` | `DateTime?` | rental start date (only for rental products) |
| `rentalEndDate` | `DateTime?` | rental end date (only for rental products) |

### CheckoutAttributeConditionsModel (ShoppingCart)

Represents the state of the conditional checkout attributes

| Feld | Typ | Beschreibung |
|---|---|---|
| `enabledAttributeIds` | `IList<int>` | identifiers of the conditional attributes whose condition is met |
| `disabledAttributeIds` | `IList<int>` | identifiers of the conditional attributes whose condition is not met |

### CheckoutAttributesRequest (ShoppingCart)

Represents a request to save the checkout attributes of the shopping cart

| Feld | Typ | Beschreibung |
|---|---|---|
| `attributes` | `Dictionary<string, string>` | selected checkout attributes, using the same keys as the storefront form, e.g. "checkout_attribute_1": "5" (value id of a dropdown/radio/color/image squares attribute), "checkout_attribute_2": "6,7" (checkboxes), "checkout_attribute_3": "text", "checkout_attribute_4_day": "31", "checkout_attribute_4_month": "12", "checkout_attribute_4_year": "2026" (date picker), "checkout_attribute_5": "download GUID" (file upload, see cart/checkout-attributes/{attributeId}/upload). Attributes not passed are removed |

### CheckoutAttributesResultModel (ShoppingCart)

Represents the result of saving the checkout attributes

| Feld | Typ | Beschreibung |
|---|---|---|
| `cart` | `ShoppingCartModel` | updated shopping cart |
| `enabledAttributeIds` | `IList<int>` | identifiers of the conditional attributes whose condition is met (to be displayed) |
| `disabledAttributeIds` | `IList<int>` | identifiers of the conditional attributes whose condition is not met (to be hidden) |
| `warnings` | `IList<string>` | warnings of the selected attributes (e.g. required attributes not selected), which prevent the checkout |

### CouponCodeRequest (ShoppingCart)

Represents a request to apply a discount coupon code or a gift card code

| Feld | Typ | Beschreibung |
|---|---|---|
| `code` | `string` |  |

### EstimateShippingRequest (ShoppingCart)

Represents a request to estimate the shipping of the shopping cart

| Feld | Typ | Beschreibung |
|---|---|---|
| `countryId` | `int?` |  |
| `stateProvinceId` | `int?` |  |
| `zipPostalCode` | `string` |  |
| `city` | `string` | city (required instead of the ZIP code if "Use city name for shipping estimation" is enabled) |
| `checkoutAttributes` | `Dictionary<string, string>` | checkout attributes to save before (see <see cref="CheckoutAttributesRequest.Attributes"/>); null to keep the currently saved ones |

### FileUploadResultModel (ShoppingCart)

Represents the result of uploading a file for a checkout attribute

| Feld | Typ | Beschreibung |
|---|---|---|
| `downloadGuid` | `Guid` | download GUID to pass as the attribute value |
| `downloadUrl` | `string` | storefront URL to download the uploaded file |
| `message` | `string` |  |

### SelectShippingOptionRequest (ShoppingCart)

Represents a request to select a shipping option returned by the shipping estimation

Erweitert `EstimateShippingRequest`, enthält also auch dessen Felder.

| Feld | Typ | Beschreibung |
|---|---|---|
| `name` | `string` | name of the shipping option (see ShippingOptions[].Name of the estimation result) |

### SetCartVendorRequest (ShoppingCart)

Represents a request to select the vendor whose products are checked out (items of other vendors are set aside)

| Feld | Typ | Beschreibung |
|---|---|---|
| `vendorId` | `int?` | vendor identifier (see AvailableVendors of the cart); null or 0 to check out the products of all vendors |

### UpdateCartItemRequest (ShoppingCart)

Represents a request to change the quantity of a shopping cart or wishlist item

| Feld | Typ | Beschreibung |
|---|---|---|
| `quantity` | `int` |  |

### StatusModel

Represents the API status model

| Feld | Typ | Beschreibung |
|---|---|---|
| `apiVersion` | `string` |  |
| `nopVersion` | `string` |  |
| `storeId` | `int` |  |
| `storeName` | `string` |  |

### AddWishlistItemsToCartRequest (Wishlist)

Represents a request to add wishlist items to the shopping cart

| Feld | Typ | Beschreibung |
|---|---|---|
| `itemIds` | `IList<int>` | identifiers of the wishlist items to add |
| `customerGuid` | `Guid?` | GUID of the customer who shared the wishlist; null for the own wishlist of the current customer |
| `listId` | `int?` | custom wishlist identifier; null for the default wishlist |

### AddWishlistItemsToCartResultModel (Wishlist)

Represents the result of adding wishlist items to the shopping cart

| Feld | Typ | Beschreibung |
|---|---|---|
| `addedItemsCount` | `int` | number of items added to the shopping cart |
| `warnings` | `IList<string>` | warnings of items that couldn't be added |
| `message` | `string` | message to display to the customer if there are warnings |
| `wishlist` | `WishlistModel` | updated wishlist (items may have been moved to the cart) |

### CustomWishlistRequest (Wishlist)

Represents a request to create or rename a custom wishlist

| Feld | Typ | Beschreibung |
|---|---|---|
| `name` | `string` | wishlist name |
| `productId` | `int?` | identifier of a product to move from the default wishlist to the new wishlist (only on creation; optional) |

### MoveWishlistItemRequest (Wishlist)

Represents a request to move a wishlist item to another wishlist

| Feld | Typ | Beschreibung |
|---|---|---|
| `listId` | `int?` | target custom wishlist identifier; null or 0 for the default wishlist |

