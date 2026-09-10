import json, pathlib, importlib, re, sys
from tree_sitter import Language, Parser
LANGS={'.py':'python','.cs':'c_sharp','.js':'javascript','.html':'javascript','.cpp':'cpp','.h':'cpp','.sh':'bash','.ps1':'powershell'}
KINDS={'function_definition','function_declaration','method_declaration','constructor_declaration','destructor_declaration','operator_declaration','conversion_operator_declaration','local_function_statement','lambda_expression','anonymous_method_expression','arrow_function','function_expression','method_definition','accessor_declaration','function_statement'}
def generate(sources,revision):
    parsers={}; rows=[]; coverage=[]
    for path,src in sorted(sources.items()):
        ext=pathlib.Path(path).suffix; lang=LANGS[ext]
        if lang not in parsers:
            mod=importlib.import_module('tree_sitter_'+lang)
            parsers[lang]=Parser(Language(mod.language()))
        sections=[(src,0)]
        if ext=='.html':
            sections=[(m.group(1),src[:m.start(1)].count('\n')) for m in re.finditer(r'<script\b[^>]*>(.*?)</script>',src,re.S|re.I)]
        errors=0; count=0
        for section,offset in sections:
            raw=section.encode(); tree=parsers[lang].parse(raw)
            stack=[tree.root_node]
            while stack:
                n=stack.pop()
                if n.type=='ERROR' or n.is_missing: errors+=1
                if n.type in KINDS or (lang=='cpp' and n.type=='function_declarator'):
                    # C++ definition already has a function declarator; keep declaration node only.
                    if lang=='cpp' and n.type=='function_definition':
                        stack.extend(reversed(n.named_children)); continue
                    name=n.child_by_field_name('name')
                    if name is None:
                        name=n.child_by_field_name('declarator')
                    body=n.child_by_field_name('body')
                    signature=raw[n.start_byte:body.start_byte if body else n.end_byte].decode()
                    if n.type in {'lambda_expression','arrow_function','anonymous_method_expression','function_expression'}:
                        signature=signature.split('=>')[0]+' =>'
                    line=n.start_point.row+1+offset
                    parents=[];p=n.parent
                    while p:
                        if p.type in {'class_declaration','struct_declaration','interface_declaration','record_declaration','namespace_declaration','class_definition','function_definition'}:
                            pn=p.child_by_field_name('name')
                            if pn:parents.append(pn.text.decode())
                        p=p.parent
                    rows.append({'path':path,'line':line,'kind':n.type,'name':name.text.decode() if name else '<anonymous/accessor>','scope':'.'.join(reversed(parents)),'signature':' '.join(signature.split()),'category':'third_party' if path.startswith(('third_party/','tools/')) else 'test' if path.startswith('tests/') else 'project'})
                    count+=1
                stack.extend(reversed(n.named_children))
        coverage.append({'path':path,'language':lang,'functions':count,'parse_errors':errors})
    return {'schema_version':'nosai.function_index.v1','source_revision':revision,'coverage':coverage,'functions':rows,'limitations':['Static syntax index, not call graph; generated/runtime functions absent.','C++ headers parsed as C++; preprocessor variants not expanded.','HTML inline scripts only; external scripts indexed separately.','Accessor and anonymous entries may lack a semantic name.','Files with parse errors require review; completeness not certified.']}
if __name__=='__main__':
    root=pathlib.Path(sys.argv[1]);revision=sys.argv[2]
    if root.is_file():
        sources=json.loads(root.read_text())
    else:
        import subprocess
        paths=subprocess.check_output(['git','ls-files'],cwd=root,text=True).splitlines()
        sources={p:(root/p).read_text(encoding='utf-8-sig') for p in paths if pathlib.Path(p).suffix in LANGS}
    result=generate(sources,revision)
    out=pathlib.Path(sys.argv[3]);out.mkdir(parents=True,exist_ok=True)
    (out/'FUNCTION_INDEX.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
    print(json.dumps({'files':len(result['coverage']),'functions':len(result['functions']),'parse_errors':[x for x in result['coverage'] if x['parse_errors']]}))
