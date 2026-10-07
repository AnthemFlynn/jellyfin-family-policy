"""Real browser form tests against the disposable native API; Jellyfin ApiClient shim only."""
from pathlib import Path
from playwright.sync_api import sync_playwright,expect
import json

def check_ui(base,admin,parent_id,root):
    html=(root/'src/FamilyPolicy.Plugin/Web/settings.html').read_text()
    with sync_playwright() as p:
        browser=p.chromium.launch()
        page=browser.new_page()
        # The installed Jellyfin web client supplies this API interface. Fetch goes to the real test server.
        config={'base':base,'token':admin,'parent':parent_id}
        page.add_init_script("window.__testConfig="+json.dumps(config)+";"+'''
        window.ApiClient={
          getUrl:p=>__testConfig.base+'/'+p,
          getCurrentUserId:()=>__testConfig.parent,
          ajax:async o=>{const r=await fetch(o.url,{method:o.type||'GET',headers:{'Authorization':'MediaBrowser Token="'+__testConfig.token+'"','Content-Type':'application/json'},body:o.data});if(!r.ok)throw new Error('HTTP '+r.status);return r.status===204?undefined:r.json();},
          getUsers:()=>ApiClient.ajax({url:ApiClient.getUrl('Users')}),
          getItems:(id,o)=>ApiClient.ajax({url:ApiClient.getUrl('Items?'+new URLSearchParams({...o,UserId:id}))})
        };''')
        page.route(base+'/ui-test.html',lambda route:route.fulfill(status=200,body=html,content_type='text/html'))
        page.goto(base+'/ui-test.html')
        page.evaluate("document.getElementById('familyPolicyPage').dispatchEvent(new Event('pageshow'))")
        expect(page.locator('#fpAccount option')).to_have_count(2)
        page.locator('#fpPg').click()
        page.locator('#fpZone').fill('UTC')
        page.locator('#fpKind').select_option('unrated-subject');page.locator('#fpValue').fill('Soccer');page.locator('#fpAdd').click()
        page.locator('#fpKind').select_option('franchise');page.locator('#fpValue').fill('Star Wars');page.locator('#fpAdd').click()
        page.locator('#fpKind').select_option('hours');page.locator('#fpAdd').click()
        page.locator('#fpKind').select_option('morning');page.locator('#fpValue').fill('Workout');page.locator('#fpAdd').click()
        draft=json.loads(page.locator('#fpPolicy').input_value())
        assert len(draft['Rules'])==5 and draft['DefaultTimeAllowed'] is False
        assert draft['Rules'][-1]['When']['All'][1]['Window']['End']=='08:00:00'
        page.locator('#fpSearch').fill('Soccer');page.locator('#fpFind').click()
        expect(page.locator('#fpMedia option')).to_have_count(1)
        page.locator('#fpPreview').click()
        expect(page.locator('#fpExplanation')).to_contain_text('Content: allowed')
        # Saving confirmed labels must preserve an unsaved draft and enforce revision checks.
        page.locator('#fpSubject').fill('Soccer');page.locator('#fpLabels').click()
        expect(page.locator('#fpResult')).to_contain_text('Parent-confirmed labels saved')
        assert len(json.loads(page.locator('#fpPolicy').input_value())['Rules'])==5
        page.locator('#fpRules button').last.click()
        assert len(json.loads(page.locator('#fpPolicy').input_value())['Rules'])==4
        browser.close()
    return {'check':'Chromium rule-editor workflow','preset_and_advanced_rules':True,'preview_uses_native_api':True,'label_save_preserves_draft':True}
