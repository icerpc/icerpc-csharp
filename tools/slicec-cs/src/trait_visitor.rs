
use crate::builders::{AttributeBuilder, CommentBuilder, ContainerBuilder};
use crate::comments::doc_comment_message;
use crate::generated_code::GeneratedCode;
use crate::slicec_ext::EntityExt;

use slice::grammar::*;
use slice::visitor::Visitor;

#[derive(Debug)]
pub struct TraitVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl<'a> Visitor for TraitVisitor<'a> {
    fn visit_trait(&mut self, trait_def: &Trait) {
        let trait_interface_name = trait_def.interface_name();
        let access = trait_def.access_modifier();

        let container_type = format!("{} partial interface", access);
        let mut trait_interface = ContainerBuilder::new(&container_type, &trait_interface_name);
        trait_interface
            .add_comment("summary", &doc_comment_message(trait_def))
            .add_container_attributes(trait_def)
            .add_base("IceRpc.Slice.ITrait".to_owned());

        self.generated_code.insert_scoped(trait_def, trait_interface.build().into());
    }
}
