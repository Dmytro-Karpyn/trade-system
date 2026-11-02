// Real cookie/API UI smoke. Run only against an isolated disposable dataset.
import assert from 'node:assert/strict'
import { mkdir, writeFile, readFile } from 'node:fs/promises'
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE_PATH ?? 'playwright-core')
const base = process.env.AUDIT_BASE_URL ?? 'https://localhost:8447', password = process.env.AUDIT_ADMIN_PASSWORD
assert.ok(password, 'Supply the isolated development seed password through env')
const output = process.env.AUDIT_OUTPUT ?? '/tmp/trade-ui-consistency/artifacts'
await mkdir(output, { recursive: true })
const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH ?? '/usr/bin/chromium', args: ['--no-sandbox'] })
const context = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1440, height: 1050 }, timezoneId: 'Europe/Kyiv', locale: 'uk-UA' })
const page = await context.newPage(), errors = [], warnings = [], expected = [], checks = []
page.on('pageerror', error => errors.push(error.message))
page.on('console', message => {
  if (message.type() === 'warning') warnings.push(message.text())
  if (message.type() === 'error') {
    if (message.location().url.includes('/auth/me') && message.text().includes('401')) expected.push('Anonymous /auth/me = 401 before login')
    else errors.push(message.text() + ' ' + message.location().url)
  }
})
page.on('requestfailed', request => { if (request.failure()?.errorText !== 'net::ERR_ABORTED') errors.push(request.url() + ': ' + request.failure()?.errorText) })
async function api(path, data, method = data === undefined ? 'GET' : 'POST') {
  let headers = {}
  if (method !== 'GET') { const csrf = await (await context.request.get(base + '/api/auth/csrf')).json(); headers[csrf.headerName] = csrf.requestToken }
  const response = await context.request.fetch(base + '/api' + path, { method, data, headers })
  assert.ok(response.ok(), `${path}: ${response.status()} ${await response.text()}`)
  return response.status() === 204 ? undefined : response.json()
}
function pass(message) { checks.push(message); console.log('PASS ' + message) }
async function shot(name) {
  await page.screenshot({ path: `${output}/${name}.png`, animations: 'disabled' })
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, 'Horizontal page overflow: ' + name)
}
async function choose(label,name) {
  await page.getByRole('combobox',{name:label,exact:true}).click()
  await page.getByRole('option',{name,exact:true}).click()
}
async function filters() {
  await page.getByRole('button',{name:'Фільтри',exact:true}).click()
  const sheet=page.getByRole('dialog',{name:'Фільтри',exact:true})
  await sheet.waitFor()
  await sheet.getByRole('button',{name:'Показати результати',exact:true}).click()
  await sheet.waitFor({state:'hidden'})
}
const visited=[],contrastChecks=[]
function contrast(foreground,background) {
  const luminance=color=>{const values=color.match(/[\d.]+/g).slice(0,3).map(Number).map(value=>{const channel=value/255;return channel<=0.04045?channel/12.92:((channel+0.055)/1.055)**2.4});return values[0]*0.2126+values[1]*0.7152+values[2]*0.0722}
  const values=[luminance(foreground),luminance(background)].sort((a,b)=>a-b)
  return (values[1]+0.05)/(values[0]+0.05)
}
async function visit(path,width=1440,screenshot=false) {
  await page.setViewportSize({width,height:width===1440?1050:844})
  await page.goto(base+path)
  await page.waitForLoadState('networkidle')
  await page.getByRole('main').getByRole('heading',{level:1}).waitFor()
  assert.equal(await page.getByText('Не вдалося відкрити сторінку',{exact:true}).count(),0,'No runtime boundary: '+path)
  assert.equal(await page.getByText('Недостатньо прав',{exact:true}).count(),0,'Admin can access: '+path)
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth),false,'Body overflow: '+path+' at '+width)
  assert.equal(await page.locator('main button.MuiIconButton-root').evaluateAll(nodes=>nodes.filter(node=>node.getBoundingClientRect().width>0&&!node.getAttribute('aria-label')&&!node.getAttribute('title')).length),0,'Icon names: '+path)
  if(width<1200) await page.getByRole('navigation',{name:'Мобільна навігація',exact:true}).waitFor()
  for(const summary of await page.getByRole('region',{name:'Підсумки продажу',exact:true}).all()) {const box=await summary.boundingBox();if(box) assert.ok(box.width<=500,'Compact sale summary surface: '+path)}
  const controls=await page.locator('main .MuiOutlinedInput-root:not(.MuiInputBase-multiline)').evaluateAll(nodes=>nodes.filter(node=>node.getBoundingClientRect().width>0).map(node=>node.getBoundingClientRect().height))
  if(controls.length) assert.ok(Math.max(...controls)-Math.min(...controls)<=1,'Consistent single-line field heights: '+path+' '+controls.join(','))
  if(width<900) for(const height of await page.locator('main button.MuiButton-root').evaluateAll(nodes=>nodes.filter(node=>node.getBoundingClientRect().width>0).map(node=>node.getBoundingClientRect().height))) assert.ok(height>=44,'44px mobile button: '+path)
  const inks=await page.locator('main .MuiButton-contained:not(:disabled), main [data-status]').evaluateAll(nodes=>nodes.filter(node=>node.getBoundingClientRect().width>0).map(node=>({foreground:getComputedStyle(node).color,background:getComputedStyle(node).backgroundColor})))
  for(const ink of inks) {const ratio=contrast(ink.foreground,ink.background);assert.ok(ratio>=4.5,'Text contrast '+path+': '+ratio);contrastChecks.push({path,width,ratio:Number(ratio.toFixed(2))})}
  visited.push({path,width,result:'passed'})
  if(screenshot) await shot(`${width===1440?'desktop':'mobile-'+width}-${path.replace(/[^a-z0-9]+/gi,'-').replace(/^-|-$/g,'')||'dashboard'}`)
}
async function cancelDialog(action) {
  await page.getByRole('button',{name:action,exact:true}).click()
  const dialog=page.getByRole('dialog')
  await dialog.waitFor()
  if(['Видалити','Розпровести'].includes(action)) assert.equal(await dialog.getByRole('button',{name:action,exact:true}).evaluate(node=>getComputedStyle(node).backgroundColor),'rgb(196, 90, 82)','Muted destructive button')
  const box=await dialog.boundingBox(),viewport=page.viewportSize()
  assert.ok(box&&box.x>=0&&box.y>=0&&box.x+box.width<=viewport.width+1&&box.y+box.height<=viewport.height+1,'Dialog within viewport')
  await dialog.getByRole('button',{name:'Скасувати',exact:true}).click()
  await dialog.waitFor({state:'hidden'})
}
async function sticky(action) {
  await page.locator('#main-content').evaluate(async main=>{main.scrollTop=main.scrollHeight;await new Promise(requestAnimationFrame);await new Promise(requestAnimationFrame)})
  const box=await page.getByRole('button',{name:action,exact:true}).boundingBox(),nav=await page.getByRole('navigation',{name:'Мобільна навігація',exact:true}).boundingBox()
  assert.ok(box&&nav&&box.height>=44&&box.y>=64&&box.y+box.height<=nav.y+1,'Sticky action above navigation: '+action)
}
try {
  await page.goto(base+'/login')
  await page.getByLabel('Логін',{exact:true}).fill('admin')
  await page.getByLabel('Пароль',{exact:true}).fill(password)
  await page.getByRole('button',{name:'Увійти',exact:true}).click()
  await page.getByRole('heading',{name:'Огляд діяльності',exact:true}).waitFor()
  let f
  try { f=JSON.parse(await readFile('/tmp/trade-ui-consistency/fixtures.json','utf8')) } catch { /* Fresh isolated database. */ }
  if(f) {const probe=await context.request.get(base+'/api/products/'+f.goods.id);if(probe.status()===404) f=undefined;else assert.ok(probe.ok(),'Fixture probe must succeed before reuse')}
  if(!f) {
    const stamp=Date.now().toString().slice(-7),date=new Date().toISOString().slice(0,10),uah='00000000-0000-0000-0000-000000000980'
    const unit=await api('/units',{name:'шт '+stamp,active:true}),root=await api('/categories',{name:'Квіти '+stamp,active:true,sortOrder:0})
    const child=await api('/categories',{name:'Зрізані '+stamp,parentId:root.id,description:'Категорія аудиту',sortOrder:1,active:true})
    const source=await api('/storage-locations',{name:'Основний '+stamp,active:true}),destination=await api('/storage-locations',{name:'Магазин '+stamp,active:true}),price=await api('/price-types',{name:'Роздрібна '+stamp,defaultMarkupPercent:25,active:true,sortOrder:0})
    const supplier=await api('/counterparties',{name:'Постачальник '+stamp,isSupplier:true}),buyer=await api('/counterparties',{name:'Покупець '+stamp,isCustomer:true})
    const goods=await api('/products',{unitId:unit.id,categoryId:child.id,itemType:'GOODS',code:'G-'+stamp,name:'Троянда '+stamp}),service=await api('/products',{unitId:unit.id,itemType:'SERVICE',code:'S-'+stamp,name:'Оформлення '+stamp})
    await api('/products',{unitId:unit.id,itemType:'SERVICE',code:'OLD-'+stamp,name:'Неактивна послуга '+stamp,active:false})
    await api(`/products/${service.id}/prices/${price.id}`,{unitPrice:300.1234},'PUT')
    const purchaseInput={documentType:'PURCHASE_INVOICE',documentDate:date,counterpartyId:supplier.id,storageLocationId:source.id,currencyId:uah,exchangeRate:1,discountPercent:10,lines:[{lineNo:1,productId:goods.id,quantity:30,unitPriceCurrency:100}]}
    let purchase=await api('/purchases',purchaseInput);purchase=await api(`/purchases/${purchase.document.id}/post`,{})
    const purchaseDraft=await api('/purchases',{...purchaseInput,lines:[{lineNo:1,productId:goods.id,quantity:2,unitPriceCurrency:100}]})
    let opening=await api('/purchases',{...purchaseInput,documentType:'OPENING_STOCK',counterpartyId:null,storageLocationId:destination.id,lines:[{lineNo:1,productId:goods.id,quantity:1,unitPriceCurrency:100}]});opening=await api(`/purchases/${opening.document.id}/post`,{})
    const [batch]=await api(`/purchases/${purchase.document.id}/batches`)
    const saleInput={documentDate:date,counterpartyId:buyer.id,storageLocationId:source.id,priceTypeId:price.id,discountPercent:5,lines:[{lineNo:1,productId:goods.id,batchId:batch.batchId,quantity:2,unitPrice:80},{lineNo:2,productId:service.id,quantity:1}]}
    let sale=await api('/sales',saleInput);sale=await api(`/sales/${sale.document.id}/post`,{})
    const saleDraft=await api('/sales',{...saleInput,lines:[{lineNo:1,productId:goods.id,batchId:batch.batchId,quantity:1}]})
    const category=await api('/expense-categories',{name:'Доставка '+stamp,active:true}),reason=await api('/write-off-reasons',{name:'Пошкодження '+stamp,expenseCategoryId:category.id,active:true})
    const expense=await api('/expenses',{documentDate:date,counterpartyId:supplier.id,expenseCategoryId:category.id,currencyId:uah,amount:100.1234,exchangeRate:1,purchaseInvoiceId:purchase.document.id})
    const customerReturn=await api('/customer-returns',{documentDate:date,sourceSaleInvoiceId:sale.document.id,storageLocationId:source.id,lines:[{sourceSaleLineId:sale.lines[0].id,quantity:1},{sourceSaleLineId:sale.lines[1].id,quantity:0,refundAmount:50}]})
    const supplierReturn=await api('/supplier-returns',{documentDate:date,sourcePurchaseInvoiceId:purchase.document.id,storageLocationId:source.id,lines:[{sourcePurchaseLineId:purchase.lines[0].id,quantity:1}]})
    const transfer=await api('/stock-transfers',{documentDate:date,fromLocationId:source.id,toLocationId:destination.id,lines:[{lineNo:1,batchId:batch.batchId,quantity:1}]})
    const writeOff=await api('/write-offs',{documentDate:date,storageLocationId:source.id,writeOffReasonId:reason.id,lines:[{lineNo:1,batchId:batch.batchId,quantity:1}]})
    const cash=await api('/money/accounts',{name:'Каса '+stamp,accountType:'CASH',currencyId:uah,active:true}),bank=await api('/money/accounts',{name:'Банк '+stamp,accountType:'BANK',currencyId:uah,active:true})
    const negative=await api('/money/payments',{documentDate:date,purpose:'OWNER',sourceAccountId:cash.id,sourceAmount:50.1234});await api(`/money/payments/${negative.document.id}/post`,{})
    const receipt=await api('/money/receipts',{documentDate:date,purpose:'SETTLEMENT',destinationAccountId:cash.id,destinationAmount:100,counterpartyId:buyer.id,settlementCurrencyId:uah,settlementAmount:100,settlementExchangeRate:1})
    const payment=await api('/money/payments',{documentDate:date,purpose:'OWNER',sourceAccountId:cash.id,sourceAmount:20})
    const moneyTransfer=await api('/money/transfers',{documentDate:date,sourceAccountId:cash.id,destinationAccountId:bank.id,sourceAmount:50,destinationAmount:50})
    const role=await api('/admin/roles',{name:'Оператор '+stamp,active:true});await api(`/admin/roles/${role.id}/permissions`,{permissionCodes:['product.view']},'PUT')
    const worker=await api('/admin/users',{username:'worker'+stamp,password,fullName:'Оператор '+stamp,active:false,roleIds:[role.id]})
    await api(`/price-types/${price.id}`,{name:price.name,defaultMarkupPercent:35,active:true,sortOrder:0},'PUT')
    f={goods,service,source,destination,price,buyer,supplier,purchase,purchaseDraft,batch,sale,saleDraft,expense,customerReturn,supplierReturn,transfer,writeOff,receipt,payment,moneyTransfer,worker,role}
    await writeFile('/tmp/trade-ui-consistency/fixtures.json',JSON.stringify(f))
  }
  pass('Real cookie/CSRF login and isolated fixtures for all business/admin modules')
  const reports=['sales','profitability','stock','purchases','money','expenses','settlements'],directories=['categories','units','price-types','storage-locations','write-off-reasons','currencies']
  const routes=['/','/sales','/sales/new','/sales/'+f.sale.document.id,'/sales/'+f.saleDraft.document.id,'/purchases','/purchases/new','/purchases/'+f.purchase.document.id,'/purchases/'+f.purchaseDraft.document.id,'/products','/products/new','/products/'+f.goods.id,'/products/'+f.service.id,'/stock','/stock/products/'+f.goods.id,'/stock/batches/'+f.batch.batchId,'/stock/transfers','/stock/transfers/new','/stock/transfers/'+f.transfer.document.id,'/stock/write-offs','/stock/write-offs/new','/stock/write-offs/'+f.writeOff.document.id,'/money','/money/accounts','/money/receipts','/money/receipts/new','/money/receipts/'+f.receipt.document.id,'/money/payments','/money/payments/'+f.payment.document.id,'/money/transfers','/money/transfers/'+f.moneyTransfer.document.id,'/settlements','/settlements/'+f.buyer.id,'/expenses','/expenses/new','/expenses/'+f.expense.document.id,'/customer-returns','/customer-returns/'+f.customerReturn.document.id,'/supplier-returns','/supplier-returns/'+f.supplierReturn.document.id,'/reports',...reports.map(kind=>'/reports/'+kind),'/directories',...directories.map(kind=>'/directories/'+kind),'/expense-categories','/admin','/admin/users','/admin/users/new','/admin/users/'+f.worker.userId,'/admin/roles','/admin/roles/'+f.role.id,'/admin/audit','/counterparties','/counterparties/new','/counterparties/'+f.buyer.id]
  const keyScreens=new Set(['/','/sales/new','/products/'+f.service.id,'/purchases/'+f.purchase.document.id,'/stock','/stock/batches/'+f.batch.batchId,'/money','/settlements','/expenses/'+f.expense.document.id,'/reports/profitability','/directories/categories','/admin/users','/admin/audit','/counterparties'])
  for(const route of routes) await visit(route,1440,keyScreens.has(route))
  pass(`Desktop: ${routes.length} routes, headers, forms, statuses, named icons and no horizontal body overflow`)
  for(const [root,tableName,detail] of [['/sales','Продажі','Вибраний продаж'],['/purchases','Закупівлі','Вибрана закупівля'],['/products','Товари','Вибрана номенклатура'],['/money/receipts','Грошові документи','Вибраний грошовий документ'],['/expenses','Витрати','Вибрана витрата'],['/customer-returns','Повернення покупців','Вибране повернення'],['/supplier-returns','Повернення постачальникам','Вибране повернення'],['/admin/users','Користувачі','Вибраний користувач'],['/admin/roles','Ролі','Вибрана роль'],['/directories/categories','Категорії товарів','Редактор довідника']]) {
    await visit(root)
    const row=page.getByRole('table',{name:tableName,exact:true}).locator('tbody tr').first()
    await row.focus();await page.keyboard.press('Enter')
    await page.getByRole('region',{name:detail,exact:true}).getByRole('heading').first().waitFor()
    assert.equal(await row.getAttribute('aria-selected'),'true')
    assert.match(await row.locator('td').first().evaluate(node=>getComputedStyle(node).boxShadow),/inset/,'Non-color selection marker')
  }
  pass('Ten desktop split selections: keyboard, aria-selected, structural marker and shared detail headers')
  await visit('/sales/'+f.sale.document.id);await cancelDialog('Розпровести')
  await visit('/sales/'+f.saleDraft.document.id);await cancelDialog('Провести');await cancelDialog('Видалити')
  await page.goto(base+'/');await page.waitForLoadState('networkidle');await page.keyboard.press('Tab')
  assert.equal(await page.evaluate(()=>document.activeElement.textContent),'Перейти до вмісту')
  assert.notEqual(await page.evaluate(()=>getComputedStyle(document.activeElement).outlineStyle),'none')
  pass('POST/UNPOST/delete confirmations remain bounded; keyboard skip link has visible focus')
  const primary=['/','/sales','/purchases','/products','/stock','/money','/settlements','/expenses','/customer-returns','/supplier-returns','/reports/sales','/directories','/admin/users','/admin/roles','/admin/audit','/counterparties']
  const details=[['/sales/'+f.saleDraft.document.id,'Зберегти'],['/purchases/'+f.purchaseDraft.document.id,'Зберегти'],['/expenses/'+f.expense.document.id,'Зберегти'],['/money/receipts/'+f.receipt.document.id,'Зберегти'],['/stock/transfers/'+f.transfer.document.id,'Зберегти'],['/stock/write-offs/'+f.writeOff.document.id,'Зберегти'],['/customer-returns/'+f.customerReturn.document.id,'Зберегти'],['/supplier-returns/'+f.supplierReturn.document.id,'Зберегти'],['/admin/users/'+f.worker.userId,'Зберегти користувача'],['/admin/roles/'+f.role.id,'Зберегти роль']]
  for(const width of [390,430]) {
    for(const route of primary) {
      await visit(route,width,width===390)
      if(!['/','/money','/directories'].includes(route)) await filters()
    }
    for(const [route,action] of details) {await visit(route,width);await sticky(action)}
    await visit('/products/'+f.service.id,width,true)
    await page.getByRole('button',{name:'Редагувати ціну '+f.price.name,exact:true}).click()
    const editor=page.getByRole('dialog');await editor.getByRole('spinbutton',{name:'Ціна, UAH',exact:true}).waitFor()
    const box=await editor.boundingBox();assert.ok(box&&box.x>=0&&box.x+box.width<=width+1,'Price dialog not clipped')
    await editor.getByRole('button',{name:'Скасувати',exact:true}).click();await editor.waitFor({state:'hidden'})
    await page.getByRole('button',{name:'Ще — відкрити решту меню',exact:true}).click()
    await page.getByRole('navigation',{name:'Основна навігація',exact:true}).waitFor()
    await page.getByRole('button',{name:'Закрити меню',exact:true}).click()
    await page.getByRole('navigation',{name:'Основна навігація',exact:true}).waitFor({state:'hidden'})
    await visit('/admin/audit',width)
    await page.getByRole('list',{name:'Події аудиту',exact:true}).getByRole('button',{name:'Деталі',exact:true}).first().click()
    await page.getByRole('dialog',{name:'Деталі події',exact:true}).getByRole('list',{name:'Зміни полів',exact:true}).waitFor()
    await shot('mobile-'+width+'-audit-diff')
    await page.getByRole('button',{name:'Закрити деталі',exact:true}).click()
  }
  pass('390×844 and 430×844: all primary modules, sheets/cards, ten sticky document/editor actions, More drawer and unclipped dialogs')
  assert.deepEqual(errors,[],'Unexpected browser errors');assert.deepEqual(warnings,[],'Unexpected browser warnings')
  pass('Zero unexpected console/network/page errors and zero React/MUI warnings')
} catch(error) {
  await page.screenshot({path:`${output}/failure.png`}).catch(()=>{})
  await writeFile(`${output}/failure.txt`,await page.locator('body').innerText()).catch(()=>{})
  throw error
} finally {
  await writeFile(`${output}/result.json`,JSON.stringify({checks,visited,contrastChecks,errors,warnings,expected},null,2))
  await browser.close()
}
