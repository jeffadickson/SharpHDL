(set-logic QF_BV)
(set-option :produce-models true)
(set-option :timeout 5000)
(declare-fun input_0 () (_ BitVec 32))
(assert (let ((a!1 (ite (= ((_ sign_extend 16) ((_ extract 15 0) input_0)) input_0)
                #b1
                #b0))
      (a!2 (bvnot (bvor (ite (bvslt input_0 #xffff8000) #b1 #b0)
                        (ite (bvslt #x00007fff input_0) #b1 #b0)))))
  (not (= a!1 (bvnot a!2)))))

(check-sat)
(get-model)
